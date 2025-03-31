using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using PDG_Elevation_Builder.Models;
using PDG_Elevation_Builder.UI;
using PDGMethods;

namespace PDG_Elevation_Builder
{
    /// <summary>
    /// Handles the generation of reference plans from elevation views
    /// </summary>
    public class ReferencePlanGenerator
    {
        private Document _doc;

        /// <summary>
        /// Constructor for the Reference Plan Generator
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        public ReferencePlanGenerator(Document doc)
        {
            _doc = doc;
        }

        /// <summary>
        /// Creates reference plans for a list of elevations based on configurations
        /// </summary>
        /// <param name="configurations">List of reference plan configurations</param>
        /// <returns>Dictionary mapping elevation IDs to lists of created plan view IDs</returns>
        public Dictionary<ElementId, List<ElementId>> CreateReferencePlans(List<ReferencePlanConfiguration> configurations)
        {
            Dictionary<ElementId, List<ElementId>> result = new Dictionary<ElementId, List<ElementId>>();

            foreach (var config in configurations)
            {
                List<ElementId> planViews = CreateReferencePlansForElevation(config);
                if (planViews.Any())
                {
                    result.Add(config.ElevationId, planViews);
                }
            }

            return result;
        }

        /// <summary>
        /// Creates reference plans for a single elevation based on its configuration
        /// </summary>
        /// <param name="config">Reference plan configuration</param>
        /// <returns>List of created plan view IDs</returns>
        private List<ElementId> CreateReferencePlansForElevation(ReferencePlanConfiguration config)
        {
            List<ElementId> createdViews = new List<ElementId>();

            // Get the elevation view
            ViewSection elevationView = _doc.GetElement(config.ElevationId) as ViewSection;
            if (elevationView == null)
            {
                return createdViews;
            }

            // Get the elevation's level
            Level level = GetElevationLevel(elevationView);
            if (level == null)
            {
                return createdViews;
            }

            // Process each cut height
            foreach (double cutHeight in config.CutHeights)
            {
                // Create a plan view at the specified height
                ViewPlan planView = CreateReferencePlan(elevationView, level, cutHeight, config.PlanExtent);
                if (planView != null)
                {
                    createdViews.Add(planView.Id);
                }
            }

            return createdViews;
        }

        /// <summary>
        /// Creates a reference plan for a specific elevation at the given height
        /// </summary>
        /// <param name="elevationView">The elevation view</param>
        /// <param name="level">The reference level</param>
        /// <param name="cutHeight">Height above level for the cut plane</param>
        /// <param name="planExtent">Distance from wall for plan extent</param>
        /// <returns>The created plan view</returns>
        private ViewPlan CreateReferencePlan(ViewSection elevationView, Level level, double cutHeight, double planExtent)
        {
            // Get view direction and location
            XYZ viewDirection = elevationView.ViewDirection;
            Transform viewTransform = elevationView.CropBox.Transform;

            // Get the view family type for floor plans
            ViewFamilyType vft = GetFloorPlanViewFamilyType();
            if (vft == null)
            {
                return null;
            }

            // Create a new plan view
            ViewPlan planView = ViewPlan.Create(_doc, vft.Id, level.Id);
            if (planView == null)
            {
                return null;
            }

            // Set the view name - include elevation name and height
            string planName = $"REF PLAN {elevationView.Name} - {cutHeight:0.00}'";
            planView.Name = GetUniqueViewName(planName);

            // Set view template if available
            PDGMethods.Views.SetViewTemplate(_doc, planView, "CD23 - Partition Plan");

            // Set cut plane height
            PlanViewRange viewPR = planView.GetViewRange();
            viewPR.SetOffset(PlanViewPlane.CutPlane, cutHeight);
            planView.CropBoxActive = true;
            planView.CropBoxVisible = true;

            // Adjust crop box to match the elevation width and specified depth
            AdjustPlanViewCropBox(planView, elevationView, planExtent);

            return planView;
        }

        /// <summary>
        /// Adjusts the crop box of a plan view to match the elevation view width and specified depth
        /// </summary>
        /// <param name="planView">The plan view to adjust</param>
        /// <param name="elevationView">The reference elevation view</param>
        /// <param name="planExtent">Distance from wall for plan extent</param>
        private void AdjustPlanViewCropBox(ViewPlan planView, ViewSection elevationView, double planExtent)
        {
            // Get elevation parameters
            XYZ originalViewDir = elevationView.ViewDirection;
            BoundingBoxXYZ elevationBox = elevationView.CropBox;
            Transform elevationTransform = elevationBox.Transform;

            XYZ ogCBMax = elevationView.CropBox.Max;
            XYZ ogCBMin = elevationView.CropBox.Min;

            // Get elevation dimensions
            double elevationWidth = Math.Abs(elevationBox.Max.X - elevationBox.Min.X);

            // Create a new crop box for the plan view
            BoundingBoxXYZ planCropBox = planView.CropBox;
            
            // Calculate the X min/max (width dimension aligned with the elevation view width)
            double halfWidth = elevationWidth / 2.0;

            double farClipOffset = 0;

            double cropOffset = 0.167;
            // Adjust crop box dimensions based on view orientation
            // We need to maintain the transform orientation while changing the size

            string input = Parameters.GetInstanceOrTypeParameterByName(elevationView, "Elevation_Origin").AsValueString();
            
            string xPart = input.Split(',')[0];
            string yPart = input.Split(',')[1];

            double markerX = 0, markerY = 0;


            // Handle x value - check for negative sign explicitly
            bool xIsNegative = xPart.StartsWith("-");
            if (xIsNegative)
                xPart = xPart.Substring(1); // Remove the negative sign

            if (double.TryParse(xPart, out markerX))
                markerX = xIsNegative ? -markerX : markerX; // Apply negative sign if needed

            // Handle y value in the same way
            bool yIsNegative = yPart.StartsWith("-");
            if (yIsNegative)
                yPart = yPart.Substring(1); // Remove the negative sign

            if (double.TryParse(yPart, out markerY))
                markerY = yIsNegative ? -markerY : markerY; // Apply negative sign if needed

            string quadrant = DetermineQuadrant(markerX, markerY);
            string direction = DetermineViewOrientation(elevationView.ViewDirection);


            switch (quadrant)
            {
                case "Q1":
                    switch (direction)
                    {
                        case "North":
                            planCropBox.Min = new XYZ(ogCBMin.X, (elevationView.Origin.Y + (0 - ogCBMin.Z)) - planExtent, planCropBox.Min.Z);
                            planCropBox.Max = new XYZ(ogCBMax.X, elevationView.Origin.Y + (0 - ogCBMin.Z), planCropBox.Max.Z);
                            break;
                        case "South":
                            planCropBox.Min = new XYZ(ogCBMin.X + (ogCBMax.X - ogCBMin.X) - (1.5 * cropOffset), (elevationView.Origin.Y - (0 - ogCBMin.Z)), planCropBox.Min.Z);
                            planCropBox.Max = new XYZ(ogCBMax.X + (ogCBMax.X - ogCBMin.X) - (1.5 * cropOffset), elevationView.Origin.Y - (0 - ogCBMin.Z) + planExtent, planCropBox.Max.Z);
                            break;
                        case "East":
                            planCropBox.Min = new XYZ(elevationView.Origin.X - (0 - ogCBMin.Z), ogCBMin.X, planCropBox.Min.Z);
                            planCropBox.Max = new XYZ(elevationView.Origin.X - (0 - ogCBMin.Z) + planExtent, ogCBMax.X, planCropBox.Max.Z);
                            break;
                        case "West":
                            planCropBox.Min = new XYZ(elevationView.Origin.X + (0 - ogCBMin.Z) - planExtent, ogCBMin.X + (0 - ogCBMin.X) - (1 * cropOffset), planCropBox.Min.Z);
                            planCropBox.Max = new XYZ(elevationView.Origin.X + (0 - ogCBMin.Z), ogCBMax.X + (0 - ogCBMin.X) - (1 * cropOffset), planCropBox.Max.Z);
                            break;
                    }
                    break;
                case "Q2":
                    switch (direction)
                    {
                        case "North":
                            planCropBox.Min = new XYZ(ogCBMin.X, (elevationView.Origin.Y + (0 - ogCBMin.Z)) - planExtent, planCropBox.Min.Z);
                            planCropBox.Max = new XYZ(ogCBMax.X, elevationView.Origin.Y + (0 - ogCBMin.Z), planCropBox.Max.Z+1);
                            break;
                        case "South":
                            planCropBox.Min = new XYZ(0-Math.Abs(ogCBMax.X), (elevationView.Origin.Y - (0 - ogCBMin.Z)), planCropBox.Min.Z);
                            planCropBox.Max = new XYZ(0 - Math.Abs(ogCBMin.X), elevationView.Origin.Y - (0 - ogCBMin.Z) + planExtent, planCropBox.Max.Z+1);
                            break;
                        case "East":
                            planCropBox.Min = new XYZ(elevationView.Origin.X + (0 - ogCBMin.Z) - planExtent, (0-ogCBMax.X), planCropBox.Min.Z);
                            planCropBox.Max = new XYZ(elevationView.Origin.X + (0 - ogCBMin.Z), Math.Abs(ogCBMin.X), planCropBox.Max.Z+1);
                            break;
                        case "West":
                            planCropBox.Min = new XYZ(elevationView.Origin.X + ogCBMin.Z, ogCBMin.X, planCropBox.Min.Z);
                            planCropBox.Max = new XYZ(elevationView.Origin.X + ogCBMin.Z + planExtent, ogCBMax.X, planCropBox.Max.Z + 1);
                            break;
                    }
                    break;
                case "Q3":
                    TaskDialog.Show("Quadrant", "Elevation in Q3");
                    break;
                case "Q4":
                    TaskDialog.Show("Quadrant", "Elevation in Q4");
                    break;
            }
            // Apply the crop box
            planView.CropBox = planCropBox;
        }

        /// <summary>
        /// Gets the view family type for floor plans
        /// </summary>
        /// <returns>ViewFamilyType for floor plans</returns>
        private ViewFamilyType GetFloorPlanViewFamilyType()
        {
            // Get all view family types
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            collector.OfClass(typeof(ViewFamilyType));

            // Find a view family type for floor plans
            foreach (ViewFamilyType vft in collector)
            {
                if (vft.ViewFamily == ViewFamily.FloorPlan)
                {
                    return vft;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the elevation marker for a view section
        /// </summary>
        /// <param name="elevationView">Elevation view</param>
        /// <returns>The elevation marker</returns>
        private ElevationMarker GetElevationMarker(ViewSection elevationView)
        {
            // Get all elevation markers in the document
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            collector.OfClass(typeof(ElevationMarker));

            // Find the marker that contains this elevation view
            foreach (ElevationMarker marker in collector)
            {
                for (int i = 0; i < 4; i++)
                {
                    ElementId viewId = marker.GetViewId(i);
                    if (viewId != null && viewId.Equals(elevationView.Id))
                    {
                        return marker;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the plan view containing a specific elevation marker and gets its center coordinates
        /// </summary>
        /// <param name="doc">The Revit document</param>
        /// <param name="marker">The specific elevation marker to find</param>
        /// <returns>Tuple containing the marker and its center position</returns>
        public static (View hostView, XYZ center) GetElevationMarkerCenter(Document doc, ElevationMarker marker)
        {
            if (marker == null || doc == null)
                return (null, XYZ.Zero);

            // Get all plan views in the document
            List<ViewPlan> planViews = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.CanBePrinted)
                .ToList();

            // Check each plan view for the marker
            foreach (ViewPlan view in planViews)
            {
                // Check if this specific marker belongs to this view
                if (marker.OwnerViewId == view.Id)
                {
                    // Calculate center from bounding box
                    BoundingBoxXYZ bb = marker.get_BoundingBox(view);
                    if (bb != null)
                    {
                        XYZ center = new XYZ(
                            (bb.Min.X + bb.Max.X) / 2.0,
                            (bb.Min.Y + bb.Max.Y) / 2.0,
                            (bb.Min.Z + bb.Max.Z) / 2.0);
                        return (view, center);
                    }
                }
            }

            // If marker isn't found in any view, return null and zero
            return (null, XYZ.Zero);
        }


        /// <summary>
        /// Gets the XYZ location of an elevation marker
        /// </summary>
        /// <param name="marker">The elevation marker</param>
        /// <returns>XYZ location of the marker</returns>
        private XYZ GetElevationMarkerLocation(ElevationMarker marker, ViewPlan planView)
        {
            XYZ position = new XYZ();
            
            // Alternatively, try to get location from bounding box
            BoundingBoxXYZ bb = marker.get_BoundingBox(planView);
            if (bb != null)
            {
                // Use the center of the bounding box
                position = new XYZ(
                    (bb.Min.X + bb.Max.X) / 2.0,
                    (bb.Min.Y + bb.Max.Y) / 2.0,
                    (bb.Min.Z + bb.Max.Z) / 2.0);
                return position;
            }

            // If all else fails, try to get location from element parameters
            Parameter xParam = marker.LookupParameter("X");
            Parameter yParam = marker.LookupParameter("Y");
            Parameter zParam = marker.LookupParameter("Z");

            if (xParam != null && yParam != null && zParam != null)
            {
                position = new XYZ(
                    xParam.AsDouble(),
                    yParam.AsDouble(),
                    zParam.AsDouble());
                return position;
            }

            return null;
        }

        /// <summary>
        /// Gets a View from the document by its ElementId
        /// </summary>
        /// <param name="viewId">The ElementId of the view to retrieve</param>
        /// <returns>The View object, or null if not found or not a View</returns>
        private View GetViewById(ElementId viewId)
        {
            if (viewId == null || viewId == ElementId.InvalidElementId)
                return null;

            // Get the element from the document
            Element element = _doc.GetElement(viewId);

            // Check if the element is a View
            if (element is View view)
            {
                return view;
            }

            return null;
        }
        /// <summary>
        /// Gets the level associated with an elevation view
        /// </summary>
        /// <param name="elevationView">Elevation view</param>
        /// <returns>The associated level</returns>
        private Level GetElevationLevel(ViewSection elevationView)
        {
            // Try to get associated level parameter
            Parameter levelParam = elevationView.LookupParameter("Associated Level");
            if (levelParam != null && levelParam.HasValue)
            {
                ElementId levelId = levelParam.AsElementId();
                if (levelId != ElementId.InvalidElementId)
                {
                    return _doc.GetElement(levelId) as Level;
                }
            }

            // If no level parameter, find the lowest level in the project
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            collector.OfClass(typeof(Level));

            // Get the lowest level by elevation
            return collector.Cast<Level>()
                .OrderBy(l => l.Elevation)
                .FirstOrDefault();
        }

        /// <summary>
        /// Generates a unique view name if the proposed name already exists
        /// </summary>
        /// <param name="proposedName">The initially proposed view name</param>
        /// <returns>A unique view name</returns>
        private string GetUniqueViewName(string proposedName)
        {
            // If the name doesn't conflict, use it
            if (!ViewNameExists(proposedName))
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
            } while (ViewNameExists(newName));

            return newName;
        }

        private string TestForViewName(string proposedName)
        {
            // If the name doesn't conflict, use it
            if (!ViewNameExists(proposedName))
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
            } while (ViewNameExists(newName));

            return newName;
        }

        /// <summary>
        /// Checks if a view name already exists in the document
        /// </summary>
        /// <param name="viewName">View name to check</param>
        /// <returns>True if the name exists, false otherwise</returns>
        private bool ViewNameExists(string viewName)
        {
            // Get all views in the document
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
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
        /// Determines which quadrant a point is in based on its X and Y coordinates
        /// </summary>
        /// <param name="x">X coordinate of the point</param>
        /// <param name="y">Y coordinate of the point</param>
        /// <returns>Quadrant as a string: "Q1" (+x,+y), "Q2" (-x,+y), "Q3" (-x,-y), or "Q4" (+x,-y)</returns>
        private string DetermineQuadrant(double x, double y)
        {
            if (x >= 0 && y >= 0)
                return "Q1"; // First quadrant: +x, +y
            else if (x < 0 && y >= 0)
                return "Q2"; // Second quadrant: -x, +y
            else if (x < 0 && y < 0)
                return "Q3"; // Third quadrant: -x, -y
            else // (x >= 0 && y < 0)
                return "Q4"; // Fourth quadrant: +x, -y
        }

        private string DetermineViewOrientation(XYZ viewDir)
        {
            // Normalize the vector components for more reliable comparison
            viewDir = viewDir.Normalize();

            // Check cardinal directions based on the primary components of the vector
            if (Math.Abs(viewDir.Y) > Math.Abs(viewDir.X))
            {
                // Primarily Y-oriented
                return viewDir.Y > 0 ? "South" : "North";
            }
            else
            {
                // Primarily X-oriented
                return viewDir.X > 0 ? "West" : "East";
            }
        }
    }
}