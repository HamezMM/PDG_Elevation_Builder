using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using PDG_Elevation_Builder.UI;
using PDG_Shared_Methods;
using PDGMethods;

namespace PDG_Elevation_Builder
{
    /// <summary>
    /// Handles the generation of elevation views from room objects
    /// </summary>
    public class ElevationViewGenerator
    {
        private Document _doc;
        private ProjectNorthOrientation _northOrientation;
        private const double _defaultOffset = 0.167; // Default offset in feet
        private HashSet<string> _existingViewNames = new HashSet<string>(); // Cache of existing view names

        /// <summary>
        /// Constructor for the Elevation View Generator
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        /// <param name="northOrientation">User-specified project north orientation</param>
        public ElevationViewGenerator(Document doc, ProjectNorthOrientation northOrientation)
        {
            _doc = doc;
            _northOrientation = northOrientation;

            // Initialize the hash set if needed
            if (_existingViewNames == null)
            {
                _existingViewNames = new HashSet<string>();
            }

            // Cache all existing view names to check for conflicts
            LoadExistingViewNames();
        }

        /// <summary>
        /// Loads all existing view names from the document to check for conflicts
        /// </summary>
        private void LoadExistingViewNames()
        {
            if (_existingViewNames == null)
            {
                _existingViewNames = new HashSet<string>();
            }
            else
            {
                _existingViewNames.Clear();
            }

            // Get all views in the document
            FilteredElementCollector viewCollector = new FilteredElementCollector(_doc);
            viewCollector.OfClass(typeof(View));

            // Store all view names
            foreach (View view in viewCollector)
            {
                if (view != null && !string.IsNullOrEmpty(view.Name))
                {
                    _existingViewNames.Add(view.Name);
                }
            }
        }

        /// <summary>
        /// Creates elevation views for a list of room IDs
        /// </summary>
        /// <param name="roomIds">List of room element IDs</param>
        /// <returns>Dictionary of created elevations mapped to room IDs and a list of any name conflicts</returns>
        public (Dictionary<ElementId, List<ElementId>>, List<string>) CreateElevationsFromRoomList(List<ElementId> roomIds)
        {
            Dictionary<ElementId, List<ElementId>> result = new Dictionary<ElementId, List<ElementId>>();
            List<string> nameConflicts = new List<string>();

            if (roomIds == null || roomIds.Count == 0)
            {
                return (result, nameConflicts);
            }

            // Make sure the view names cache is initialized
            if (_existingViewNames == null || _existingViewNames.Count == 0)
            {
                LoadExistingViewNames();
            }

            foreach (ElementId roomId in roomIds)
            {
                if (roomId == null) continue;

                Room room = _doc.GetElement(roomId) as Room;
                if (room != null && room.Area > 0)
                {
                    var roomResults = CreateElevationsForRoom(roomId);
                    var elevationViewIds = roomResults.Item1;
                    var conflicts = roomResults.Item2;

                    if (elevationViewIds != null && elevationViewIds.Count > 0)
                    {
                        result.Add(roomId, elevationViewIds);
                    }

                    // Collect any name conflicts
                    if (conflicts != null && conflicts.Count > 0)
                    {
                        nameConflicts.AddRange(conflicts);
                    }
                }
            }

            return (result, nameConflicts);
        }

        /// <summary>
        /// Creates elevation markers for a single room
        /// </summary>
        /// <param name="roomId">Room element ID</param>
        /// <returns>List of created elevation view IDs and any name conflicts</returns>
        private (List<ElementId>, List<string>) CreateElevationsForRoom(ElementId roomId)
        {
            List<ElementId> elevationIds = new List<ElementId>();
            List<string> nameConflicts = new List<string>();

            Room room = _doc.GetElement(roomId) as Room;
            if (room == null || room.Area <= 0)
                return (elevationIds, nameConflicts);

            // Get room boundaries
            IList<IList<BoundarySegment>> boundaries = GetRoomBoundarySegments(room);
            if (boundaries == null || boundaries.Count == 0)
                return (elevationIds, nameConflicts);

            // Get room bounding box to determine dimensions
            BoundingBoxXYZ roomBB = room.get_BoundingBox(null);
            if (roomBB == null)
                return (elevationIds, nameConflicts);

            // Calculate room center point
            XYZ minPoint = roomBB.Min;
            XYZ maxPoint = roomBB.Max;
            XYZ centerPoint = new XYZ(
                (minPoint.X + maxPoint.X) / 2.0,
                (minPoint.Y + maxPoint.Y) / 2.0,
                (minPoint.Z));

            double cropOffset = 0.167;

            // Get room level
            Level level = room.Level;

            // Use a FilteredElementCollector to find views on the specific level
            ViewPlan roomView = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => v.ViewType == ViewType.FloorPlan &&
                            !v.IsTemplate &&
                            v.GenLevel?.Id == level.Id)
                .FirstOrDefault();

            // Get the view ID (or null if no view was found)
            ElementId viewId = roomView?.Id;

            // Get the view family type for elevations
            ViewFamilyType vft = GetElevationViewFamilyType();
            if (vft == null)
                return (elevationIds, nameConflicts);

            // Create elevation marker at the center of the room
            FamilySymbol markerSymbol = GetElevationMarkerSymbol();
            if (markerSymbol == null)
                return (elevationIds, nameConflicts);

            // Ensure the symbol is active
            if (!markerSymbol.IsActive)
                markerSymbol.Activate();

            // Create the elevation marker
            ElevationMarker marker = ElevationMarker.CreateElevationMarker(_doc, vft.Id, centerPoint, 48);

            // Create elevation views from the marker
            for (int i = 0; i < 4; i++)
            {
                ViewSection elevationView = marker.CreateElevation(_doc, viewId, i);

                // Store original view direction
                XYZ originalViewDir = elevationView.ViewDirection;

                // Get view orientation (North, East, South, West)
                string orientation = GetCompassDirection(originalViewDir);

                int index = room.Name.Split(' ').Count();

                string roomName = "";

                for (int j = 0; j<index-1; j++)
                {
                    roomName += room.Name.Split(' ')[j];
                    roomName += " ";
                }

                // Generate view name
                string proposedName = $"RM{room.Number} - {roomName}- {orientation} Elevation";

                // Check for name conflicts
                string uniqueName = GetUniqueViewName(proposedName);
                if (uniqueName != proposedName)
                {
                    nameConflicts.Add($"'{proposedName}' renamed to '{uniqueName}'");
                }

                // Set view name
                elevationView.Name = uniqueName.ToUpper();

                // Set view template
                PDGMethods.Views.SetViewTemplate(_doc, elevationView, "CD32 - Interior Elevation");

                // Add to existing names for future conflict checks
                _existingViewNames.Add(uniqueName);

                // Create a new empty crop box
                BoundingBoxXYZ newCropBox = elevationView.CropBox;

                // Important: Copy the transform from the original crop box
                // This preserves the orientation of the view
                Transform transform = newCropBox.Transform;

                // Get current crop box dimensions
                XYZ min = elevationView.CropBox.Min;
                XYZ max = elevationView.CropBox.Max;

                string? direction = null;

                if (((int)originalViewDir.X) == 0 && ((int)originalViewDir.Y) == 1)
                {
                    direction = "Up";
                }
                else if (((int)originalViewDir.X) == 0 && ((int)originalViewDir.Y) == -1)
                {
                    direction = "Down";
                }
                else if (((int)originalViewDir.X) == -1 && ((int)originalViewDir.Y) == 0)
                {
                    direction = "Left";
                }
                else if (((int)originalViewDir.X) == 1 && ((int)originalViewDir.Y) == 0)
                {
                    direction = "Right";
                }

                // Adjust crop box dimensions based on view orientation
                // We need to maintain the transform orientation while changing the size
                switch (direction)
                {
                    case "Up": // North
                        newCropBox.Min = new XYZ(-minPoint.X - (maxPoint.X-minPoint.X) - cropOffset, level.Elevation - cropOffset, -(maxPoint.Y - minPoint.Y));
                        newCropBox.Max = new XYZ(-minPoint.X + cropOffset, ((int)roomBB.Max.Z) + cropOffset, max.Z);
                        break;
                    case "Down": // South
                        // For North/South elevations, expand width (X) and adjust height (Z)
                        newCropBox.Min = new XYZ(minPoint.X - cropOffset, level.Elevation - cropOffset, -(maxPoint.Y - minPoint.Y));
                        newCropBox.Max = new XYZ(maxPoint.X + cropOffset, ((int)roomBB.Max.Z) + cropOffset, max.Z);
                        break;
                    case "Right": // East
                        newCropBox.Min = new XYZ(minPoint.Y - cropOffset, level.Elevation - cropOffset, -(maxPoint.X - minPoint.X));
                        newCropBox.Max = new XYZ(maxPoint.Y + cropOffset, ((int)roomBB.Max.Z) + cropOffset, max.Z);
                        break;
                    case "Left": // West
                        // For East/West elevations, expand width (Y) and adjust height (Z)
                        newCropBox.Min = new XYZ(-minPoint.Y - cropOffset - (maxPoint.Y - minPoint.Y), level.Elevation - cropOffset, -(maxPoint.X - minPoint.X));
                        newCropBox.Max = new XYZ(-minPoint.Y + (maxPoint.Y - minPoint.Y) + cropOffset - (maxPoint.Y - minPoint.Y), ((int)roomBB.Max.Z) + cropOffset, max.Z);
                        break;
                }

                // Apply the new crop box
                elevationView.CropBox = newCropBox;

                elevationView.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR).Set(Math.Abs((maxPoint.Y - minPoint.Y)/2)+cropOffset);

                // Add to result list
                elevationIds.Add(elevationView.Id);
            }
            return (elevationIds, nameConflicts);
        }

        /// <summary>
        /// Generates a unique view name if the proposed name already exists
        /// </summary>
        /// <param name="proposedName">The initially proposed view name</param>
        /// <returns>A unique view name</returns>
        private string GetUniqueViewName(string proposedName)
        {
            // Ensure the proposedName isn't null
            if (string.IsNullOrEmpty(proposedName))
            {
                proposedName = "Unnamed Elevation";
            }

            // Ensure existingViewNames is initialized
            if (_existingViewNames == null)
            {
                _existingViewNames = new HashSet<string>();
                LoadExistingViewNames();
            }

            // If the name doesn't already exist, use it
            if (!_existingViewNames.Contains(proposedName))
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
            } while (_existingViewNames.Contains(newName));

            return newName;
        }

        /// <summary>
        /// Gets the first elevation marker family symbol
        /// </summary>
        /// <returns>FamilySymbol for elevation marker</returns>
        private FamilySymbol GetElevationMarkerSymbol()
        {
            // Get all elevation marker symbols
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            collector.OfCategory(BuiltInCategory.OST_ElevationMarks);
            collector.OfClass(typeof(FamilySymbol));

            return collector.FirstElement() as FamilySymbol;
        }

        /// <summary>
        /// Gets the appropriate view family type for elevations
        /// </summary>
        /// <returns>ViewFamilyType for elevations</returns>
        private ViewFamilyType GetElevationViewFamilyType()
        {
            // Get all view family types
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
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

        public static List<FamilySymbol> GetAllElevationMarkerTypes(Document doc)
        {
            List<FamilySymbol> elevationMarkerTypes = new List<FamilySymbol>();

            // Get all family symbols in the document that are elevation markers
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(FamilySymbol));

            // Filter for only elevation marker family symbols
            foreach (FamilySymbol familySymbol in collector)
            {
                // Check if the family symbol belongs to an elevation marker family
                if (familySymbol.Family != null &&
                    familySymbol.Family.FamilyCategory != null &&
                    familySymbol.Family.FamilyCategory.Id.IntegerValue == (int)BuiltInCategory.OST_ElevationMarks)
                {
                    elevationMarkerTypes.Add(familySymbol);
                }
            }

            return elevationMarkerTypes;
        }

        /// <summary>
        /// Creates a section box for an elevation view of a room
        /// </summary>
        /// <param name="room">The room</param>
        /// <param name="viewDirection">Direction of view</param>
        /// <returns>BoundingBoxXYZ to use for the section</returns>
        private BoundingBoxXYZ GetSectionBoxForElevation(Room room, XYZ viewDirection)
        {
            // Get room bounding box
            BoundingBoxXYZ roomBB = room.get_BoundingBox(null);

            // Get room dimensions
            XYZ min = roomBB.Min;
            XYZ max = roomBB.Max;

            // Calculate room center
            XYZ center = new XYZ(
                (min.X + max.X) / 2.0,
                (min.Y + max.Y) / 2.0,
                (min.Z + max.Z) / 2.0);

            // Create a section bounding box based on the view direction
            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();

            // Calculate the far clipping offset - make it large enough to catch the whole room
            double farClipOffset = Math.Max(max.X - min.X, max.Y - min.Y) * 1.5;

            // Create a transform based on the view direction
            XYZ rightDirection;

            if (viewDirection.IsAlmostEqualTo(XYZ.BasisY) || viewDirection.IsAlmostEqualTo(XYZ.BasisY.Negate()))
            {
                rightDirection = XYZ.BasisX;
            }
            else
            {
                rightDirection = XYZ.BasisY;
            }

            // Set min and max extents
            sectionBox.Min = new XYZ(-farClipOffset / 2, -farClipOffset / 2, min.Z - 1.0);
            sectionBox.Max = new XYZ(farClipOffset / 2, farClipOffset / 2, max.Z + 3.0); // Add height for ceiling

            // Create transform for the view
            Transform transform = Transform.Identity;
            transform.BasisX = rightDirection;
            transform.BasisY = XYZ.BasisZ;
            transform.BasisZ = viewDirection.Negate(); // Look in opposite direction
            transform.Origin = center;

            sectionBox.Transform = transform;

            return sectionBox;
        }

        /// <summary>
        /// Gets the boundary segments from a room
        /// </summary>
        /// <param name="room">Room element</param>
        /// <returns>List of room boundary segments</returns>
        private IList<IList<BoundarySegment>> GetRoomBoundarySegments(Room room)
        {
            // Get the room boundaries
            SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions();
            options.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish;

            // Get boundary segments
            IList<IList<BoundarySegment>> boundaries = room.GetBoundarySegments(options);
            return boundaries;
        }

        /// <summary>
        /// Converts project coordinates to compass directions based on user-selected north orientation
        /// </summary>
        /// <param name="direction">Direction vector</param>
        /// <returns>Named compass direction</returns>
        private string GetCompassDirection(XYZ direction)
        {
            // Normalize the direction vector
            XYZ normalizedDir = direction.Normalize();

            // Calculate the angle from the X axis in degrees
            double angle = (Math.Atan2(normalizedDir.Y, normalizedDir.X) * 180 / Math.PI) + 90;

            // Adjust angle based on the project north orientation
            // Each orientation rotates the angle by 90 degrees
            switch (_northOrientation)
            {
                case ProjectNorthOrientation.East:
                    angle -= 270;
                    break;
                case ProjectNorthOrientation.South:
                    angle -= 180;
                    break;
                case ProjectNorthOrientation.West:
                    angle -= 90;
                    break;
                    // North is the default, no adjustment needed
            }

            // Normalize angle to 0-360 range
            angle = (angle + 360) % 360;

            // Determine the compass direction based on the angle
            if (angle >= 315 || angle < 45)
                return "North";
            else if (angle >= 45 && angle < 135)
                return "West";
            else if (angle >= 135 && angle < 225)
                return "South";
            else
                return "East";
        }
    }
}