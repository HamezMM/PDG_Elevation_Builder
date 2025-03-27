using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PDG_Elevation_Builder.UI;

namespace PDG_Elevation_Builder
{
    /// <summary>
    /// Command for the PDG Reference Plan Creator addin
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ReferencePlanCreatorCommand : IExternalCommand
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

                // Show the elevation selection window
                ElevationSelectionWindow window = new ElevationSelectionWindow(uiDoc);

                // Show dialog as modal
                bool? dialogResult = window.ShowDialog();

                // Process dialog result (the actual creation happens in the PlanConfigurationWindow)
                if (dialogResult == true)
                {
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

        internal static PushButtonData GetButtonData()
        {
            // use this method to define the properties for this command in the Revit ribbon
            string buttonInternalName = "btnReferencePlanCreator";
            string buttonTitle = "Reference Plan Creator";

            Common.ButtonDataClass myButtonData = new Common.ButtonDataClass(
                buttonInternalName,
                buttonTitle,
                typeof(ReferencePlanCreatorCommand).FullName,
                Properties.Resources.Blue_32, // Use an appropriate icon
                Properties.Resources.Blue_16, // Use an appropriate icon
                "Create reference plans from elevation views");

            return myButtonData.Data;
        }
    }
}