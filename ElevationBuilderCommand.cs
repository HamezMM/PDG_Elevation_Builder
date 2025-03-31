using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PDG_Elevation_Builder.UI;
using PDG_Shared_Methods;

namespace PDG_Elevation_Builder
{
    /// <summary>
    /// Main command for the PDG Elevation Builder addin
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ElevationBuilderCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Get the Revit application and document
                UIApplication uiApp = commandData.Application;
                UIDocument uiDoc = uiApp.ActiveUIDocument;
                Document doc = uiDoc.Document;

                // Check if the current document is a valid project file
                if (doc.IsFamilyDocument)
                {
                    TaskDialog.Show("Error", "This command can only be used in a project environment.");
                    return Result.Failed;
                }

                // Show the room selection window
                RoomSelectionWindow window = new RoomSelectionWindow(uiDoc);

                // Show dialog as modal
                bool? dialogResult = window.ShowDialog();

                // Process dialog result
                if (dialogResult == true)
                {
                    // Get selected rooms and orientation
                    List<RoomViewModel> selectedRooms = window.SelectedRooms;
                    ProjectNorthOrientation orientation = window.SelectedOrientation;

                    // Process the rooms and create elevations
                    CreateElevationsForRooms(uiDoc, selectedRooms, orientation);

                    return Result.Succeeded;
                }
                else
                {
                    // User cancelled
                    return Result.Cancelled;
                }
            }
            catch (Exception ex)
            {
                // Log error and show dialog
                message = ex.Message;

                TaskDialog.Show("Error",
                    $"An error occurred while executing the command:\n\n{ex.Message}\n\n" +
                    $"Please contact support with this error message.");

                return Result.Failed;
            }
        }

        /// <summary>
        /// Creates elevation views for the selected rooms
        /// </summary>
        /// <param name="uiDoc">Current Revit UIDocument</param>
        /// <param name="selectedRooms">List of selected room view models</param>
        /// <param name="orientation">Project north orientation</param>
        private void CreateElevationsForRooms(
             UIDocument uiDoc,
             List<RoomViewModel> selectedRooms,
             ProjectNorthOrientation orientation)
        {
            Document doc = uiDoc.Document;

            // Collection to track created elements
            Dictionary<string, int> createdElements = new Dictionary<string, int>
            {
                { "Rooms Processed", 0 },
                { "Elevation Markers", 0 },
                { "Elevation Views", 0 }
            };

            // List to track renamed views
            List<string> nameConflicts = new List<string>();

            // Create an instance of the ElevationViewGenerator
            ElevationViewGenerator generator = new ElevationViewGenerator(doc, orientation);

            // Collect room IDs
            List<ElementId> roomIds = selectedRooms.Select(r => r.Id).ToList();

            // Start transaction
            using (Transaction trans = new Transaction(doc, "Create Room Elevations"))
            {
                trans.Start();

                try
                {
                    // Create elevations for all selected rooms
                    var (createdElevations, conflicts) = generator.CreateElevationsFromRoomList(roomIds);
                    nameConflicts = conflicts;

                    // Update counts for reporting
                    createdElements["Rooms Processed"] = createdElevations.Count;
                    createdElements["Elevation Markers"] = createdElevations.Count; // One marker per room

                    // Count total elevation views
                    int totalViews = 0;
                    foreach (var viewList in createdElevations.Values)
                    {
                        totalViews += viewList.Count;
                    }
                    createdElements["Elevation Views"] = totalViews;

                    // If enabled, create plan views showing elevation locations
                    // (Will be implemented in a future step with SectionPlanViewCreator)

                    // If enabled, create sheets for the elevations
                    // (Will be implemented in a future step with SheetGenerator)

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    trans.RollBack();

                    TaskDialog.Show("Error",
                        $"An error occurred while creating elevations:\n\n{ex.Message}");

                    return;
                }
            }

            // Create the completion message
            StringBuilder message = new StringBuilder();
            message.AppendLine($"Successfully processed:");
            message.AppendLine($"- {createdElements["Rooms Processed"]} rooms");
            message.AppendLine($"- Created {createdElements["Elevation Markers"]} elevation markers");
            message.AppendLine($"- Generated {createdElements["Elevation Views"]} elevation views");

            double markerTime = createdElements["Elevation Markers"] * 6.0;

            double viewTime = createdElements["Elevation Views"] * 26.5;

            double manualTime = viewTime + markerTime;

            // Factor the time saved into decimal hours
            double timeSaved = manualTime * 0.00027777777777777778;

            // Get current user
            string username = Environment.UserName;

            Task.Run(async () =>
            {
                var fields = AirtableUtils.LogCommandRun(
                    "Place Room Elevations",
                    username,
                    timeSaved,
                    Result.Succeeded
                );

                await AirtableUtils.AddRecord(
                    "patTfknaTE8PDMSf0.94bd050d6f7949ae2f4f64a24b8f3a85b83212cd3f08f91430220c3d624dee86",
                    "appzT98QjKTqecJ0V",
                    "tblz8Be6kFE3IQ1Jc",
                    fields
                );
            }).Wait();

            // Add information about renamed views if any
            if (nameConflicts.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("The following views were renamed to avoid name conflicts:");

                // Limit the number of conflicts shown to avoid overwhelming the user
                int maxConflictsToShow = Math.Min(nameConflicts.Count, 10);
                for (int i = 0; i < maxConflictsToShow; i++)
                {
                    message.AppendLine($"- {nameConflicts[i]}");
                }

                if (nameConflicts.Count > maxConflictsToShow)
                {
                    message.AppendLine($"- And {nameConflicts.Count - maxConflictsToShow} more conflicts...");
                }
            }

            // Show success message with summary
            TaskDialog.Show("Elevation Creation Complete", message.ToString());
        }

        internal static PushButtonData GetButtonData()
        {
            // use this method to define the properties for this command in the Revit ribbon
            string buttonInternalName = "btnCommand1";
            string buttonTitle = "Create Room\rElevations";

            Common.ButtonDataClass myButtonData = new Common.ButtonDataClass(
                buttonInternalName,
                buttonTitle,
                MethodBase.GetCurrentMethod().DeclaringType?.FullName,
                Properties.Resources.Blue_32,
                Properties.Resources.Blue_16,
                "This is a tooltip for Button 1");

            return myButtonData.Data;
        }
    }

}
