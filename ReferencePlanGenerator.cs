using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using PDG_Elevation_Builder.Models;
using PDG_Elevation_Builder.UI;

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

            // Get the elevation marker and direction
            ElevationMarker marker = GetElevationMarker(elevationView);
            if (marker == null)
            {
                return createdViews;
            }

            // Get view direction and location
            XYZ viewDirection = elevationView.ViewDirection;
            Transform viewTransform = elevationView.CropBox.Transform;
            Location location = marker.Location;
            LocationPoint locationPoint = (LocationPoint)location;
            XYZ markerLocation = locationPoint.Point;

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
            string planName = $"REF {elevationView.Name} - {cutHeight:0.00}'";
            planView.Name = GetUniqueViewName(planName);

            // Set view template if available
            PDGMethods.Views.SetViewTemplate(_doc, planView, "CD11 - Floor Plan");

            // Set cut plane height
            planView.get_Parameter(BuiltInParameter.VIEW_PARTS_VISIBILITY).Set(1); // Show parts
            planView.CropBoxActive = true;
            planView.CropBoxVisible = true;

            // Set cut plane offset
            Parameter cutPlaneParam = planView.get_Parameter(BuiltInParameter.PLAN_VIEW_CUT_PLANE_HEIGHT);
            cutPlaneParam.Set(cutHeight);

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
            XYZ viewDirection = elevationView.ViewDirection;
            BoundingBoxXYZ elevationBox = elevationView.CropBox;
            Transform elevationTransform = elevationBox.Transform;

            // Get elevation dimensions
            double elevationWidth = Math.Abs(elevationBox.Max.X - elevationBox.Min.X);

            // Create a new crop box for the plan view
            BoundingBoxXYZ planCropBox = planView.CropBox;

            // Calculate the X min/max (width dimension aligned with the elevation view width)
            double halfWidth = elevationWidth / 2.0;

            // Transform the view direction into plan view coordinates
            XYZ planDirection = new XYZ(viewDirection.X, viewDirection.Y, 0).Normalize();

            // Calculate the perpendicular direction (which becomes the width axis)
            XYZ widthDirection = new XYZ(-planDirection.Y, planDirection.X, 0).Normalize();

            // Calculate the points of the crop box
            XYZ centerPoint = elevationTransform.Origin;

            // Get min/max points based on width and depth
            XYZ minPoint = centerPoint - widthDirection * halfWidth - planDirection * planExtent;
            XYZ maxPoint = centerPoint + widthDirection * halfWidth;

            // Set the crop box dimensions
            planCropBox.Min = new XYZ(minPoint.X, minPoint.Y, planCropBox.Min.Z);
            planCropBox.Max = new XYZ(maxPoint.X, maxPoint.Y, planCropBox.Max.Z);

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
    }
}