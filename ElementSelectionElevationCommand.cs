using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using PDG_Elevation_Builder.UI;
using PDG_Shared_Methods;
using PDGMethods;

namespace PDG_Elevation_Builder
{
    /// <summary>
    /// Command for creating elevation views from a selection set of elements
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ElementSelectionElevationCommand : IExternalCommand
    {
        // Default offset distance for elevation markers (2 feet)
        private const double DEFAULT_MARKER_OFFSET = 2.0;

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

                // Prompt user to select elements
                IList<Reference> selectedReferences = null;
                try
                {
                    // Create selection options
                    SelectionFilterClass selFilter = new SelectionFilterClass();
                    selectedReferences = uiDoc.Selection.PickObjects(
                        ObjectType.Element,
                        selFilter,
                        "Select elements for elevation creation"
                    );
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // User canceled the selection
                    return Result.Cancelled;
                }

                // Check if any elements were selected
                if (selectedReferences == null || selectedReferences.Count == 0)
                {
                    TaskDialog.Show("Selection Required", "Please select at least one element to create elevations.");
                    return Result.Failed;
                }

                // Get selected elements
                List<Element> selectedElements = new List<Element>();
                foreach (Reference reference in selectedReferences)
                {
                    Element element = doc.GetElement(reference);
                    if (element != null)
                    {
                        selectedElements.Add(element);
                    }
                }

                // Ask user for marker offset distance
                double markerOffset = DEFAULT_MARKER_OFFSET;

                // Optional: Show dialog to get custom offset value
                using (TaskDialog offsetDialog = new TaskDialog("Elevation Marker Offset"))
                {
                    offsetDialog.MainInstruction = "Specify offset distance for elevation markers";
                    offsetDialog.MainContent = "Enter the distance (in feet) to offset elevation markers from the selection:";
                    offsetDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "2'-0\" (Default)");
                    offsetDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "3'-0\"");
                    offsetDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "4'-0\"");
                    offsetDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "5'-0\"");
                    offsetDialog.CommonButtons = TaskDialogCommonButtons.Cancel;
                    offsetDialog.DefaultButton = TaskDialogResult.CommandLink1;

                    TaskDialogResult result = offsetDialog.Show();

                    if (result == TaskDialogResult.Cancel)
                    {
                        return Result.Cancelled;
                    }
                    else if (result == TaskDialogResult.CommandLink1)
                    {
                        markerOffset = 2.0;
                    }
                    else if (result == TaskDialogResult.CommandLink2)
                    {
                        markerOffset = 3.0;
                    }
                    else if (result == TaskDialogResult.CommandLink3)
                    {
                        markerOffset = 4.0;
                    }
                    else if (result == TaskDialogResult.CommandLink4)
                    {
                        markerOffset = 5.0;
                    }
                }

                // Get project north orientation
                int orientationValue = ProjectParametersUtil.GetProjectOrientationValue(doc);
                ProjectNorthOrientation orientation = ProjectParametersUtil.IntToOrientation(orientationValue);

                // Process the elements and create elevations
                using (Transaction trans = new Transaction(doc, "Create Selection Elevations"))
                {
                    trans.Start();

                    // Create elevations for selected elements
                    var result = CreateElevationsForSelection(doc, selectedElements, markerOffset, orientation);

                    // Update the project orientation parameter
                    ProjectParametersUtil.SetProjectOrientationValue(doc, ProjectParametersUtil.OrientationToInt(orientation));

                    trans.Commit();

                    // Show success message
                    StringBuilder message1 = new StringBuilder();
                    message1.AppendLine($"Successfully created elevations for {selectedElements.Count} elements:");
                    message1.AppendLine($"- Created {result.Item1} elevation markers");
                    message1.AppendLine($"- Generated {result.Item2} elevation views");

                    double markerTime = result.Item1 * 6.0;
                    double viewTime = result.Item2 * 26.5;
                    double manualTime = viewTime + markerTime;
                    double timeSaved = manualTime * 0.00027777777777777778;

                    // Get current user
                    string username = Environment.UserName;

                    Task.Run(async () =>
                    {
                        var fields = AirtableUtils.LogCommandRun(
                            "Place Selection Elevations",
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

                    TaskDialog.Show("Elevation Creation Complete", message1.ToString());
                }

                return Result.Succeeded;
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
        /// Creates elevation views for selected elements
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        /// <param name="selectedElements">List of selected elements</param>
        /// <param name="markerOffset">Offset distance for elevation markers</param>
        /// <param name="orientation">Project north orientation</param>
        /// <returns>Tuple with number of markers and views created</returns>
        private Tuple<int, int> CreateElevationsForSelection(
            Document doc,
            List<Element> selectedElements,
            double markerOffset,
            ProjectNorthOrientation orientation)
        {
            int markersCreated = 0;
            int viewsCreated = 0;

            // Get combined bounding box for all selected elements
            BoundingBoxXYZ combinedBoundingBox = GetCombinedBoundingBox(selectedElements);
            if (combinedBoundingBox == null)
            {
                TaskDialog.Show("Error", "Could not calculate bounding box for selected elements.");
                return new Tuple<int, int>(0, 0);
            }

            // Get the level
            Level level = FindNearestLevel(doc, combinedBoundingBox.Min.Z);
            if (level == null)
            {
                TaskDialog.Show("Error", "Could not find a suitable level for elevations.");
                return new Tuple<int, int>(0, 0);
            }

            // Get view family type for elevations
            ViewFamilyType vft = GetElevationViewFamilyType(doc);
            if (vft == null)
            {
                TaskDialog.Show("Error", "Could not find elevation view family type.");
                return new Tuple<int, int>(0, 0);
            }

            // Get elevation marker symbol
            FamilySymbol markerSymbol = GetElevationMarkerSymbol(doc);
            if (markerSymbol == null)
            {
                TaskDialog.Show("Error", "Could not find elevation marker symbol.");
                return new Tuple<int, int>(0, 0);
            }

            // Ensure the symbol is active
            if (!markerSymbol.IsActive)
                markerSymbol.Activate();

            // Get current view
            View currentView = doc.ActiveView;
            ElementId viewId = currentView.Id;

            // Check if current view is a suitable plan view; if not, find one
            if (!(currentView is ViewPlan) || currentView.ViewType != ViewType.FloorPlan)
            {
                // Try to find a floor plan view at the same level
                FilteredElementCollector viewCollector = new FilteredElementCollector(doc);
                viewCollector.OfClass(typeof(ViewPlan));

                foreach (ViewPlan planView in viewCollector.Cast<ViewPlan>())
                {
                    if (planView.ViewType == ViewType.FloorPlan &&
                        !planView.IsTemplate &&
                        planView.GenLevel != null &&
                        planView.GenLevel.Id == level.Id)
                    {
                        viewId = planView.Id;
                        break;
                    }
                }
            }

            // Calculate marker positions and place elevation markers
            // We will place markers at the midpoints of each side of the bounding box
            // with an offset of markerOffset feet away from the bounding box

            XYZ minPoint = combinedBoundingBox.Min;
            XYZ maxPoint = combinedBoundingBox.Max;

            // Calculate midpoints of bounding box sides
            XYZ midPointNorth = new XYZ((minPoint.X + maxPoint.X) / 2, maxPoint.Y + markerOffset, minPoint.Z);
            XYZ midPointEast = new XYZ(maxPoint.X + markerOffset, (minPoint.Y + maxPoint.Y) / 2, minPoint.Z);
            XYZ midPointSouth = new XYZ((minPoint.X + maxPoint.X) / 2, minPoint.Y - markerOffset, minPoint.Z);
            XYZ midPointWest = new XYZ(minPoint.X - markerOffset, (minPoint.Y + maxPoint.Y) / 2, minPoint.Z);

            // Get selection name for view naming
            string selectionName = GetSelectionName(selectedElements);

            // Create elevation markers and views
            List<ViewSection> createdViews = new List<ViewSection>();

            // North elevation (looking south)
            ElevationMarker markerNorth = ElevationMarker.CreateElevationMarker(doc, vft.Id, midPointNorth, 48);
            markersCreated++;
            ViewSection viewNorth = markerNorth.CreateElevation(doc, viewId, 2); // Index 2 = South
            viewsCreated++;
            ConfigureElevationView(doc, viewNorth, selectionName, "North", combinedBoundingBox, markerOffset, midPointNorth);
            createdViews.Add(viewNorth);

            // East elevation (looking west)
            ElevationMarker markerEast = ElevationMarker.CreateElevationMarker(doc, vft.Id, midPointEast, 48);
            markersCreated++;
            ViewSection viewEast = markerEast.CreateElevation(doc, viewId, 3); // Index 3 = West
            viewsCreated++;
            ConfigureElevationView(doc, viewEast, selectionName, "East", combinedBoundingBox, markerOffset, midPointEast);
            createdViews.Add(viewEast);

            // South elevation (looking north)
            ElevationMarker markerSouth = ElevationMarker.CreateElevationMarker(doc, vft.Id, midPointSouth, 48);
            markersCreated++;
            ViewSection viewSouth = markerSouth.CreateElevation(doc, viewId, 0); // Index 0 = North
            viewsCreated++;
            ConfigureElevationView(doc, viewSouth, selectionName, "South", combinedBoundingBox, markerOffset, midPointSouth);
            createdViews.Add(viewSouth);

            // West elevation (looking east)
            ElevationMarker markerWest = ElevationMarker.CreateElevationMarker(doc, vft.Id, midPointWest, 48);
            markersCreated++;
            ViewSection viewWest = markerWest.CreateElevation(doc, viewId, 1); // Index 1 = East
            viewsCreated++;
            ConfigureElevationView(doc, viewWest, selectionName, "West", combinedBoundingBox, markerOffset, midPointWest);
            createdViews.Add(viewWest);

            // Generate a unique group ID for this set of elevations
            string groupId = ProjectParametersUtil.GenerateElevationGroupId();

            // Link the elevations together with the group ID
            ProjectParametersUtil.SetElevationGroupId(doc, createdViews.Select(v => v.Id).ToList(), groupId);

            return new Tuple<int, int>(markersCreated, viewsCreated);
        }

        /// <summary>
        /// Configures an elevation view with proper name, crop box, etc.
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        /// <param name="view">Elevation view to configure</param>
        /// <param name="selectionName">Name derived from selection</param>
        /// <param name="direction">Direction of the elevation</param>
        /// <param name="boundingBox">Bounding box of the selection</param>
        /// <param name="offset">Offset value for view extents</param>
        private void ConfigureElevationView(
            Document doc,
            ViewSection view,
            string selectionName,
            string direction,
            BoundingBoxXYZ boundingBox,
            double offset,
            XYZ locationPoint)
        {
            // Set view name
            string proposedName = $"{selectionName} - {direction} Elevation";
            string uniqueName = GetUniqueViewName(doc, proposedName.ToUpper());
            view.Name = uniqueName;

            // Set view template
            PDGMethods.Views.SetViewTemplate(doc, view, "CD32 - Interior Elevation");

            // Adjust crop box to fit selection
            BoundingBoxXYZ cropBox = view.CropBox;
            XYZ min = boundingBox.Min;
            XYZ max = boundingBox.Max;

            // Store original view direction
            XYZ viewDir = view.ViewDirection;

            // Adjust crop region with buffer
            double cropOffset = 0.167; // 2 inches in feet
            double heightBuffer = 2.0; // Add 2 feet above selection
            double farClipOffset = offset + cropOffset;

            // Save the marker location for future reference
            Parameter elevationOrigin = Parameters.GetInstanceOrTypeParameterByName(view, "Elevation_Origin");
            if (elevationOrigin != null)
            {
                elevationOrigin.Set($"{locationPoint.X},{locationPoint.Y}");
            }

            // Adjust crop box dimensions based on view direction
            if (direction == "North" || direction == "South")
            {
                // Expand width to cover full bounding box width plus offset
                double width = max.X - min.X;
                cropBox.Min = new XYZ(-width / 2 - cropOffset, min.Z - cropOffset, 0);
                cropBox.Max = new XYZ(width / 2 + cropOffset, max.Z + heightBuffer, 0);
            }
            else // East or West
            {
                // Expand width to cover full bounding box width plus offset
                double width = max.Y - min.Y;
                cropBox.Min = new XYZ(-width / 2 - cropOffset, min.Z - cropOffset, 0);
                cropBox.Max = new XYZ(width / 2 + cropOffset, max.Z + heightBuffer, 0);
            }

            // Apply the crop box
            view.CropBox = cropBox;

            // Set far clip offset
            view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR).Set(farClipOffset);
        }

        /// <summary>
        /// Generates a meaningful name for the selection based on element types
        /// </summary>
        /// <param name="selectedElements">Selected elements</param>
        /// <returns>A descriptive name for the selection</returns>
        private string GetSelectionName(List<Element> selectedElements)
        {
            // If only one element, try to use its name or family name
            if (selectedElements.Count == 1)
            {
                Element element = selectedElements[0];

                // Try to get name parameter
                Parameter nameParam = element.LookupParameter("Name");
                if (nameParam != null && nameParam.HasValue)
                {
                    string name = nameParam.AsString();
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }

                // If it's a family instance, use family name
                if (element is FamilyInstance familyInstance)
                {
                    return familyInstance.Symbol.Family.Name;
                }

                // Otherwise use category name
                return element.Category?.Name ?? "Element";
            }

            // For multiple elements, count by category
            Dictionary<string, int> categoryCounts = new Dictionary<string, int>();

            foreach (Element element in selectedElements)
            {
                string categoryName = element.Category?.Name ?? "Other";

                if (categoryCounts.ContainsKey(categoryName))
                {
                    categoryCounts[categoryName]++;
                }
                else
                {
                    categoryCounts[categoryName] = 1;
                }
            }

            // Find the most common category
            string mostCommonCategory = "Selection";
            int maxCount = 0;

            foreach (var kvp in categoryCounts)
            {
                if (kvp.Value > maxCount)
                {
                    maxCount = kvp.Value;
                    mostCommonCategory = kvp.Key;
                }
            }

            return $"{mostCommonCategory} Group";
        }

        /// <summary>
        /// Gets combined bounding box for all selected elements
        /// </summary>
        /// <param name="elements">List of elements</param>
        /// <returns>Combined bounding box</returns>
        private BoundingBoxXYZ GetCombinedBoundingBox(List<Element> elements)
        {
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;

            foreach (Element element in elements)
            {
                // Get element bounding box
                BoundingBoxXYZ box = element.get_BoundingBox(null);
                if (box != null)
                {
                    // Update min values
                    minX = Math.Min(minX, box.Min.X);
                    minY = Math.Min(minY, box.Min.Y);
                    minZ = Math.Min(minZ, box.Min.Z);

                    // Update max values
                    maxX = Math.Max(maxX, box.Max.X);
                    maxY = Math.Max(maxY, box.Max.Y);
                    maxZ = Math.Max(maxZ, box.Max.Z);
                }
            }

            // Check if valid bounding box was found
            if (minX == double.MaxValue || minY == double.MaxValue || minZ == double.MaxValue ||
                maxX == double.MinValue || maxY == double.MinValue || maxZ == double.MinValue)
            {
                return null;
            }

            // Create new bounding box with calculated values
            BoundingBoxXYZ result = new BoundingBoxXYZ();
            result.Min = new XYZ(minX, minY, minZ);
            result.Max = new XYZ(maxX, maxY, maxZ);

            return result;
        }

        /// <summary>
        /// Finds the nearest level to the specified Z coordinate
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        /// <param name="z">Z coordinate to find nearest level for</param>
        /// <returns>The nearest level</returns>
        private Level FindNearestLevel(Document doc, double z)
        {
            // Get all levels in the document
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(Level));

            Level nearestLevel = null;
            double minDistance = double.MaxValue;

            foreach (Level level in collector.Cast<Level>())
            {
                double distance = Math.Abs(level.Elevation - z);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestLevel = level;
                }
            }

            return nearestLevel;
        }

        /// <summary>
        /// Gets the appropriate view family type for elevations
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        /// <returns>ViewFamilyType for elevations</returns>
        private ViewFamilyType GetElevationViewFamilyType(Document doc)
        {
            // Get all view family types
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(ViewFamilyType));

            // Find a view family type for elevations
            foreach (ViewFamilyType vft in collector)
            {
                if (vft.ViewFamily == ViewFamily.Elevation)
                {
                    return vft;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the first elevation marker family symbol
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        /// <returns>FamilySymbol for elevation marker</returns>
        private FamilySymbol GetElevationMarkerSymbol(Document doc)
        {
            // Get all elevation marker symbols
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfCategory(BuiltInCategory.OST_ElevationMarks);
            collector.OfClass(typeof(FamilySymbol));

            return collector.FirstElement() as FamilySymbol;
        }

        /// <summary>
        /// Generates a unique view name if the proposed name already exists
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        /// <param name="proposedName">The initially proposed view name</param>
        /// <returns>A unique view name</returns>
        private string GetUniqueViewName(Document doc, string proposedName)
        {
            // Ensure the proposedName isn't null
            if (string.IsNullOrEmpty(proposedName))
            {
                proposedName = "Unnamed Elevation";
            }

            // If the name doesn't already exist, use it
            if (!ViewNameExists(doc, proposedName))
            {
                return proposedName;
            }

            // Otherwise append a number to make it unique
            int counter = 1;
            string newName;

            do
            {
                newName = $"{proposedName} ({counter})";
                counter++;
            } while (ViewNameExists(doc, newName));

            return newName;
        }

        /// <summary>
        /// Checks if a view name already exists in the document
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        /// <param name="viewName">View name to check</param>
        /// <returns>True if the name exists, false otherwise</returns>
        private bool ViewNameExists(Document doc, string viewName)
        {
            // Get all views in the document
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(View));

            // Check if any view has the specified name
            foreach (View view in collector)
            {
                if (view != null && view.Name.Equals(viewName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Button data for the Revit ribbon
        /// </summary>
        internal static PushButtonData GetButtonData()
        {
            string buttonInternalName = "btnSelectionElevation";
            string buttonTitle = "Selection Elevations";

            Common.ButtonDataClass myButtonData = new Common.ButtonDataClass(
                buttonInternalName,
                buttonTitle,
                typeof(ElementSelectionElevationCommand).FullName,
                Properties.Resources.Selection_Elevations_32, 
                Properties.Resources.Selection_Elevations_16,
                "Create elevation views for selected elements");

            return myButtonData.Data;
        }
    }

    /// <summary>
    /// Filter class for element selection
    /// </summary>
    public class SelectionFilterClass : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            // Accept most elements with geometry
            // Could be refined to filter specific categories
            if (element == null)
                return false;

            return element.Category != null &&
                  !element.Category.IsTagCategory &&
                  element.CanHaveTypeAssigned();
        }

        public bool AllowReference(Reference refer, XYZ point)
        {
            return true;
        }
    }
}