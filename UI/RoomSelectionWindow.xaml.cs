using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace PDG_Elevation_Builder.UI
{
    /// <summary>
    /// Interaction logic for RoomSelectionWindow.xaml
    /// </summary>
    public partial class RoomSelectionWindow : Window
    {
        private UIDocument _uiDoc;
        private Document _doc;
        private ObservableCollection<RoomViewModel> _rooms;
        private ProjectNorthOrientation _selectedOrientation = ProjectNorthOrientation.North;
        private RoomViewModel _lastSelectedRoom = null;
        private ObservableCollection<RoomViewModel> _filteredRooms;

        public List<RoomViewModel> SelectedRooms { get; private set; }
        public ProjectNorthOrientation SelectedOrientation => _selectedOrientation;

        /// <summary>
        /// Constructor for the Room Selection Window
        /// </summary>
        /// <param name="uiDoc">Current Revit UIDocument</param>
        public RoomSelectionWindow(UIDocument uiDoc)
        {
            InitializeComponent();

            _uiDoc = uiDoc;
            _doc = uiDoc.Document;

            // Get the project orientation parameter value and set the initial orientation
            int orientationValue = ProjectParametersUtil.GetProjectOrientationValue(_doc);
            _selectedOrientation = ProjectParametersUtil.IntToOrientation(orientationValue);

            LoadRooms();
            HighlightSelectedOrientation();

            // Add keyboard handler for shift selection
            RoomsListView.PreviewKeyDown += new System.Windows.Input.KeyEventHandler(RoomsListView_PreviewKeyDown);

            // Set window owner to Revit's main window for proper modal behavior
            Owner = System.Windows.Interop.HwndSource.FromHwnd(
                Autodesk.Windows.ComponentManager.ApplicationWindow).RootVisual as Window;
        }

        /// <summary>
        /// Handles keyboard input for the ListView
        /// </summary>
        private void RoomsListView_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Space)
            {
                // Toggle selection for all selected items
                foreach (RoomViewModel room in RoomsListView.SelectedItems)
                {
                    room.IsSelected = !room.IsSelected;
                }
                e.Handled = true;
            }
        }

        /// <summary>
        /// Loads all rooms from the current Revit document
        /// </summary>
        private void LoadRooms()
        {
            _rooms = new ObservableCollection<RoomViewModel>();

            // Get all rooms in the document
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            ICollection<Element> roomElements = collector.OfCategory(BuiltInCategory.OST_Rooms)
                                                       .WhereElementIsNotElementType()
                                                       .ToElements();

            // Convert Room elements to view models
            foreach (Element elem in roomElements)
            {
                Room room = elem as Room;
                if (room != null && room.Area > 0) // Only include placed rooms with area
                {
                    // Get room properties
                    string number = room.Number;

                    string levelName = (_doc.GetElement(room.LevelId) as Level)?.Name ?? "Unknown";
                    double area = UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareFeet);

                    // Try to get department parameter (if it exists)
                    string department = "";
                    Parameter deptParam = room.LookupParameter("Department");
                    if (deptParam != null && deptParam.HasValue)
                    {
                        department = deptParam.AsString();
                    }

                    // Create view model
                    RoomViewModel roomVM = new RoomViewModel
                    {
                        Id = room.Id,
                        Number = number,
                        Name = GetRoomName(room).ToUpper(), // This now has the number removed if it was redundant
                        Level = levelName,
                        Area = $"{area:F2} SF",
                        Department = department,
                        IsSelected = false,
                        Index = _rooms.Count // Store original index for shift-selection
                    };

                    _rooms.Add(roomVM);
                }
            }

            // Sort rooms by level and number
            var sortedRooms = new ObservableCollection<RoomViewModel>(
                _rooms.OrderBy(r => r.Level).ThenBy(r => r.Number));

            // Update indices after sorting
            for (int i = 0; i < sortedRooms.Count; i++)
            {
                sortedRooms[i].Index = i;
            }

            _rooms = sortedRooms;
            _filteredRooms = _rooms;

            // Set as data source
            RoomsListView.ItemsSource = _rooms;
        }

        /// <summary>
        /// Highlights the default orientation button (North)
        /// </summary>
        private void HighlightSelectedOrientation()
        {
            // Reset all buttons
            NorthButton.Background = System.Windows.Media.Brushes.LightGray;
            EastButton.Background = System.Windows.Media.Brushes.LightGray;
            SouthButton.Background = System.Windows.Media.Brushes.LightGray;
            WestButton.Background = System.Windows.Media.Brushes.LightGray;

            // Highlight selected button based on the orientation parameter
            switch (_selectedOrientation)
            {
                case ProjectNorthOrientation.North:
                    NorthButton.Background = System.Windows.Media.Brushes.LightBlue;
                    break;
                case ProjectNorthOrientation.East:
                    EastButton.Background = System.Windows.Media.Brushes.LightBlue;
                    break;
                case ProjectNorthOrientation.South:
                    SouthButton.Background = System.Windows.Media.Brushes.LightBlue;
                    break;
                case ProjectNorthOrientation.West:
                    WestButton.Background = System.Windows.Media.Brushes.LightBlue;
                    break;
            }
        }

        /// <summary>
        /// Filter rooms based on search text
        /// </summary>
        /// <param name="sender">Sender object</param>
        /// <param name="e">Event args</param>
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = SearchTextBox.Text.ToLower();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                // Show all rooms if search is empty
                RoomsListView.ItemsSource = _rooms;
                _filteredRooms = _rooms;
            }
            else
            {
                // Filter rooms based on search text
                var filteredRooms = new ObservableCollection<RoomViewModel>(
                    _rooms.Where(r =>
                        r.Number.ToLower().Contains(searchText) ||
                        r.Name.ToLower().Contains(searchText) ||
                        r.Level.ToLower().Contains(searchText) ||
                        r.Department.ToLower().Contains(searchText)));

                RoomsListView.ItemsSource = filteredRooms;
                _filteredRooms = filteredRooms;
            }
        }

        /// <summary>
        /// Handles checkbox click events for rooms
        /// </summary>
        private void RoomCheckbox_Click(object sender, RoutedEventArgs e)
        {
            // Get the clicked checkbox
            System.Windows.Controls.CheckBox checkBox = sender as System.Windows.Controls.CheckBox;
            if (checkBox == null) return;

            // Get the room view model associated with this checkbox
            RoomViewModel currentRoom = checkBox.DataContext as RoomViewModel;
            if (currentRoom == null) return;

            // Check if Shift key is pressed for batch selection
            if (Keyboard.IsKeyDown(System.Windows.Input.Key.LeftShift) || Keyboard.IsKeyDown(System.Windows.Input.Key.RightShift))
            {
                if (_lastSelectedRoom != null)
                {
                    // Determine the range of indices to select
                    int startIndex = Math.Min(_lastSelectedRoom.Index, currentRoom.Index);
                    int endIndex = Math.Max(_lastSelectedRoom.Index, currentRoom.Index);

                    // Find all room objects within this range in the current filtered collection
                    var roomsInRange = _filteredRooms.Where(r =>
                                                    r.Index >= startIndex &&
                                                    r.Index <= endIndex).ToList();

                    // Set all rooms in the range to have the same IsSelected value as the current room
                    foreach (var room in roomsInRange)
                    {
                        room.IsSelected = currentRoom.IsSelected;
                    }
                }
            }

            // Store this as the last selected room for future shift-click operations
            _lastSelectedRoom = currentRoom;
        }

        /// <summary>
        /// Selects all rooms in the current filtered view
        /// </summary>
        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (RoomViewModel room in _filteredRooms)
            {
                room.IsSelected = true;
            }
        }

        /// <summary>
        /// Clears selection for all rooms in the current filtered view
        /// </summary>
        private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (RoomViewModel room in _filteredRooms)
            {
                room.IsSelected = false;
            }
        }

        #region Orientation Button Handlers

        private void NorthButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedOrientation = ProjectNorthOrientation.North;

            // Reset all buttons
            EastButton.Background = System.Windows.Media.Brushes.LightGray;
            SouthButton.Background = System.Windows.Media.Brushes.LightGray;
            WestButton.Background = System.Windows.Media.Brushes.LightGray;

            // Highlight selected button
            NorthButton.Background = System.Windows.Media.Brushes.LightBlue;
        }

        private void EastButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedOrientation = ProjectNorthOrientation.East;

            // Reset all buttons
            NorthButton.Background = System.Windows.Media.Brushes.LightGray;
            SouthButton.Background = System.Windows.Media.Brushes.LightGray;
            WestButton.Background = System.Windows.Media.Brushes.LightGray;

            // Highlight selected button
            EastButton.Background = System.Windows.Media.Brushes.LightBlue;
        }

        private void SouthButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedOrientation = ProjectNorthOrientation.South;

            // Reset all buttons
            NorthButton.Background = System.Windows.Media.Brushes.LightGray;
            EastButton.Background = System.Windows.Media.Brushes.LightGray;
            WestButton.Background = System.Windows.Media.Brushes.LightGray;

            // Highlight selected button
            SouthButton.Background = System.Windows.Media.Brushes.LightBlue;
        }

        private void WestButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedOrientation = ProjectNorthOrientation.West;

            // Reset all buttons
            NorthButton.Background = System.Windows.Media.Brushes.LightGray;
            EastButton.Background = System.Windows.Media.Brushes.LightGray;
            SouthButton.Background = System.Windows.Media.Brushes.LightGray;

            // Highlight selected button
            WestButton.Background = System.Windows.Media.Brushes.LightBlue;
        }

        #endregion

        /// <summary>
        /// Handler for Create button click
        /// </summary>
        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            // Collect selected rooms
            SelectedRooms = new List<RoomViewModel>();

            foreach (RoomViewModel room in _rooms)
            {
                if (room.IsSelected)
                {
                    SelectedRooms.Add(room);
                }
            }

            // Validate selection
            if (SelectedRooms.Count == 0)
            {
                TaskDialog.Show("Selection Required", "Please select at least one room to create elevations.");
                return;
            }

            // Update the project orientation parameter before closing
            using (Transaction trans = new Transaction(_doc, "Update Project Orientation"))
            {
                trans.Start();

                int orientationValue = ProjectParametersUtil.OrientationToInt(_selectedOrientation);
                ProjectParametersUtil.SetProjectOrientationValue(_doc, orientationValue);

                trans.Commit();
            }

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Handler for Cancel button click
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private string GetRoomName(Room room)
        {
            int index = room.Name.Split(' ').Count();

            string roomName = "";

            for (int j = 0; j < index - 1; j++)
            {
                roomName += room.Name.Split(' ')[j];
                roomName += " ";
            }

            return roomName;
        }
    }

    /// <summary>
    /// View model for Room items in the list
    /// </summary>
    public class RoomViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public ElementId Id { get; set; }
        public string Number { get; set; }
        public string Name { get; set; }
        public string Level { get; set; }
        public string Area { get; set; }
        public string Department { get; set; }
        public int Index { get; set; } // Added to support shift-selection

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged("IsSelected");
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// Enum representing project north orientation options
    /// </summary>
    public enum ProjectNorthOrientation
    {
        North,
        East,
        South,
        West
    }
}