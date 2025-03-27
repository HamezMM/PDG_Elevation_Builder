using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace PDG_Elevation_Builder
{
    /// <summary>
    /// Utility class to handle project parameters for the Elevation Builder
    /// </summary>
    public static class ProjectParametersUtil
    {
        /// <summary>
        /// Parameter name for project elevation orientation
        /// </summary>
        public const string ORIENTATION_PARAM_NAME = "Project_Elevation_Orientation";

        /// <summary>
        /// Parameter name for elevation group ID
        /// </summary>
        public const string ELEVATION_GROUP_ID_PARAM_NAME = "Elevation_Group_ID";

        /// <summary>
        /// Generates a random 16-character alphanumeric ID for elevation groups
        /// </summary>
        /// <returns>Random 16-character ID string</returns>
        public static string GenerateElevationGroupId()
        {
            // Define characters to use in the random ID
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

            // Create a random number generator
            Random random = new Random();

            // Generate a random 16-character string
            return new string(Enumerable.Repeat(chars, 16)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        /// <summary>
        /// Gets the project elevation orientation parameter value
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        /// <returns>Orientation value (1-4) or 1 as default if not found</returns>
        public static int GetProjectOrientationValue(Document doc)
        {
            // Look for the project parameter in the ProjectInformation element
            Element projectInfo = doc.ProjectInformation;
            if (projectInfo == null)
                return 1; // Default to North/Up if project info not available

            // Try to get the parameter
            Parameter orientationParam = projectInfo.LookupParameter(ORIENTATION_PARAM_NAME);
            if (orientationParam == null || !orientationParam.HasValue)
                return 1; // Default to North/Up if parameter not found

            // Get the integer value
            int orientationValue = orientationParam.AsInteger();

            // Validate range (1-4) and return
            if (orientationValue < 1 || orientationValue > 4)
                return 1; // Default to North/Up if value is out of range

            return orientationValue;
        }

        /// <summary>
        /// Sets the project elevation orientation parameter value
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        /// <param name="orientationValue">Orientation value (1-4)</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool SetProjectOrientationValue(Document doc, int orientationValue)
        {
            // Validate input
            if (orientationValue < 1 || orientationValue > 4)
                return false;

            // Look for the project parameter in the ProjectInformation element
            Element projectInfo = doc.ProjectInformation;
            if (projectInfo == null)
                return false;

            // Try to get the parameter
            Parameter orientationParam = projectInfo.LookupParameter(ORIENTATION_PARAM_NAME);
            if (orientationParam == null)
                return false;

            // Set the parameter value
            return orientationParam.Set(orientationValue);
        }

        /// <summary>
        /// Converts ProjectNorthOrientation enum to an integer value (1-4)
        /// </summary>
        /// <param name="orientation">ProjectNorthOrientation enum value</param>
        /// <returns>Integer value (1-4)</returns>
        public static int OrientationToInt(UI.ProjectNorthOrientation orientation)
        {
            switch (orientation)
            {
                case UI.ProjectNorthOrientation.North:
                    return 1;
                case UI.ProjectNorthOrientation.East:
                    return 2;
                case UI.ProjectNorthOrientation.South:
                    return 3;
                case UI.ProjectNorthOrientation.West:
                    return 4;
                default:
                    return 1;
            }
        }

        /// <summary>
        /// Converts an integer value (1-4) to ProjectNorthOrientation enum
        /// </summary>
        /// <param name="value">Integer value (1-4)</param>
        /// <returns>ProjectNorthOrientation enum value</returns>
        public static UI.ProjectNorthOrientation IntToOrientation(int value)
        {
            switch (value)
            {
                case 1:
                    return UI.ProjectNorthOrientation.North;
                case 2:
                    return UI.ProjectNorthOrientation.East;
                case 3:
                    return UI.ProjectNorthOrientation.South;
                case 4:
                    return UI.ProjectNorthOrientation.West;
                default:
                    return UI.ProjectNorthOrientation.North;
            }
        }

        /// <summary>
        /// Sets the Elevation Group ID parameter for a list of views
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        /// <param name="elevationViewIds">List of elevation view IDs</param>
        /// <param name="groupId">Group ID to set</param>
        /// <returns>True if successful for all views, false otherwise</returns>
        public static bool SetElevationGroupId(Document doc, IEnumerable<ElementId> elevationViewIds, string groupId)
        {
            bool success = true;

            foreach (ElementId viewId in elevationViewIds)
            {
                View view = doc.GetElement(viewId) as View;
                if (view == null)
                    continue;

                // Try to get the parameter
                Parameter groupIdParam = view.LookupParameter(ELEVATION_GROUP_ID_PARAM_NAME);
                if (groupIdParam == null)
                {
                    success = false;
                    continue;
                }

                // Set the parameter value
                if (!groupIdParam.Set(groupId))
                {
                    success = false;
                }
            }

            return success;
        }
    }
}