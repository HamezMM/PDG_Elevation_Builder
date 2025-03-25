using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

            LoadRooms();
            HighlightDefaultOrientation();

            // Set window owner to Revit's main window for proper modal behavior
            Owner = System.Windows.Interop.HwndSource.FromHwnd(
                Autodesk.Windows.ComponentManager.ApplicationWindow).RootVisual as Window;
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
                    string name = room.Name;
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
                        Name = name,
                        Level = levelName,
                        Area = $"{area:F2} SF",
                        Department = department,
                        IsSelected = false
                    };

                    _rooms.Add(roomVM);
                }
            }

            // Sort rooms by level and number
            var sortedRooms = new ObservableCollection<RoomViewModel>(
                _rooms.OrderBy(r => r.Level).ThenBy(r => r.Number));
            _rooms = sortedRooms;

            // Set as data source
            RoomsListView.ItemsSource = _rooms;
        }

        /// <summary>
        /// Highlights the default orientation button (North)
        /// </summary>
        private void HighlightDefaultOrientation()
        {
            // Reset all buttons
            NorthButton.Background = System.Windows.Media.Brushes.LightGray;
            EastButton.Background = System.Windows.Media.Brushes.LightGray;
            SouthButton.Background = System.Windows.Media.Brushes.LightGray;
            WestButton.Background = System.Windows.Media.Brushes.LightGray;

            // Highlight selected button
            NorthButton.Background = System.Windows.Media.Brushes.LightBlue;
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
            }
            else
            {
                // Filter rooms based on search text
                var filteredRooms = _rooms.Where(r =>
                    r.Number.ToLower().Contains(searchText) ||
                    r.Name.ToLower().Contains(searchText) ||
                    r.Level.ToLower().Contains(searchText) ||
                    r.Department.ToLower().Contains(searchText)).ToList();

                RoomsListView.ItemsSource = filteredRooms;
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