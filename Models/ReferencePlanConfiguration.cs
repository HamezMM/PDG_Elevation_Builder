using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace PDG_Elevation_Builder.Models
{
    /// <summary>
    /// Configuration model for reference plan creation
    /// </summary>
    public class ReferencePlanConfiguration
    {
        /// <summary>
        /// The elevation view element ID
        /// </summary>
        public ElementId ElevationId { get; set; }

        /// <summary>
        /// Name of the elevation view (for display)
        /// </summary>
        public string ElevationName { get; set; }

        /// <summary>
        /// List of heights (in feet) for reference plans
        /// </summary>
        public List<double> CutHeights { get; set; } = new List<double>();

        /// <summary>
        /// Distance from wall (in feet) for the reference plan extent
        /// </summary>
        public double PlanExtent { get; set; } = 3.0; // Default to 3 feet

        /// <summary>
        /// Creates a new configuration with default values
        /// </summary>
        /// <param name="elevationId">Element ID of the elevation view</param>
        /// <param name="elevationName">Name of the elevation view</param>
        public ReferencePlanConfiguration(ElementId elevationId, string elevationName)
        {
            ElevationId = elevationId;
            ElevationName = elevationName;

            // Default to a single reference plan at 4' height
            CutHeights.Add(4.0);
        }
    }
}