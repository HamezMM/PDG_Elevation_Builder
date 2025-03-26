using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PDG_Elevation_Builder.UI;

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
                    Dictionary<ElementId, List<ElementId>> createdElevations =
                        generator.CreateElevationsFromRoomList(roomIds);

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

            // Show success message with summary
            TaskDialog.Show("Elevation Creation Complete",
                $"Successfully processed:\n" +
                $"- {createdElements["Rooms Processed"]} rooms\n" +
                $"- Created {createdElements["Elevation Markers"]} elevation markers\n" +
                $"- Generated {createdElements["Elevation Views"]} elevation views");
        }
    
        internal static PushButtonData GetButtonData()
        {
            // use this method to define the properties for this command in the Revit ribbon
            string buttonInternalName = "btnCommand1";
            string buttonTitle = "Button 1";

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
