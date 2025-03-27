using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PDG_Elevation_Builder;

namespace PDG_Elevation_Builder.UI
{
    /// <summary>
    /// Interaction logic for ElevationSelectionWindow.xaml
    /// </summary>
    public partial class ElevationSelectionWindow : Window
    {
        private UIDocument _uiDoc;
        private Document _doc;
        private ObservableCollection<ElevationViewModel> _elevations;
        private ObservableCollection<ElevationViewModel> _filteredElevations;
        private ElevationViewModel _lastSelectedElevation = null;

        public List<ElevationViewModel> SelectedElevations { get; private set; }

        /// <summary>
        /// Constructor for the Elevation Selection Window
        /// </summary>
        /// <param name="uiDoc">Current Revit UIDocument</param>
        public ElevationSelectionWindow(UIDocument uiDoc)
        {
            InitializeComponent();

            _uiDoc = uiDoc;
            _doc = uiDoc.Document;

            LoadElevations();

            // Add keyboard handler for shift selection
            ElevationsListView.PreviewKeyDown += new System.Windows.Input.KeyEventHandler(ElevationsListView_PreviewKeyDown);

            // Set window owner to Revit's main window for proper modal behavior
            Owner = System.Windows.Interop.HwndSource.FromHwnd(
                Autodesk.Windows.ComponentManager.ApplicationWindow).RootVisual as Window;
        }

        /// <summary>
        /// Loads all elevation views from the current Revit document
        /// </summary>
        private void LoadElevations()
        {
            _elevations = new ObservableCollection<ElevationViewModel>();

            // Get all views in the document
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            ICollection<Element> viewElements = collector.OfClass(typeof(View))
                                                        .WhereElementIsNotElementType()
                                                        .ToElements();

            int index = 0;
            // Filter for elevation views only
            foreach (Element elem in viewElements)
            {
                View view = elem as View;
                if (view != null &&
                    view.ViewType == ViewType.Elevation &&
                    !view.IsTemplate)
                {
                    // Get view properties
                    string scale = view.Scale.ToString();
                    string viewTypeName = "Elevation";

                    // Try to determine if interior or exterior elevation
                    if (view.Name.ToUpper().Contains("INTERIOR") || view.Name.ToUpper().Contains("INT"))
                    {
                        viewTypeName = "Interior Elevation";
                    }
                    else if (view.Name.ToUpper().Contains("EXTERIOR") || view.Name.ToUpper().Contains("EXT"))
                    {
                        viewTypeName = "Exterior Elevation";
                    }

                    // Get level if available
                    string levelName = "Unknown";
                    Parameter levelParam = view.LookupParameter("Associated Level");
                    if (levelParam != null && levelParam.HasValue)
                    {
                        ElementId levelId = levelParam.AsElementId();
                        if (levelId != ElementId.InvalidElementId)
                        {
                            Level level = _doc.GetElement(levelId) as Level;
                            if (level != null)
                            {
                                levelName = level.Name;
                            }
                        }
                    }

                    // Create view model
                    ElevationViewModel elevVM = new ElevationViewModel
                    {
                        Id = view.Id,
                        Name = view.Name,
                        ViewType = viewTypeName,
                        Scale = $"1\" = {scale}'-0\"",
                        Level = levelName,
                        IsSelected = false,
                        Index = index++
                    };

                    _elevations.Add(elevVM);
                }
            }

            // Sort elevations by name
            var sortedElevations = new ObservableCollection<ElevationViewModel>(
                _elevations.OrderBy(e => e.Name));

            // Update indices after sorting
            for (int i = 0; i < sortedElevations.Count; i++)
            {
                sortedElevations[i].Index = i;
            }

            _elevations = sortedElevations;
            _filteredElevations = _elevations;

            // Set as data source
            ElevationsListView.ItemsSource = _elevations;
        }

        /// <summary>
        /// Handles keyboard input for the ListView
        /// </summary>
        private void ElevationsListView_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                // Toggle selection for all selected items
                foreach (ElevationViewModel elevation in ElevationsListView.SelectedItems)
                {
                    elevation.IsSelected = !elevation.IsSelected;
                }
                e.Handled = true;
            }
        }

        /// <summary>
        /// Filter elevations based on search text
        /// </summary>
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = SearchTextBox.Text.ToLower();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                // Show all elevations if search is empty
                ElevationsListView.ItemsSource = _elevations;
                _filteredElevations = _elevations;
            }
            else
            {
                // Filter elevations based on search text
                var filteredElevations = new ObservableCollection<ElevationViewModel>(
                    _elevations.Where(r =>
                        r.Name.ToLower().Contains(searchText) ||
                        r.ViewType.ToLower().Contains(searchText) ||
                        r.Level.ToLower().Contains(searchText)));

                ElevationsListView.ItemsSource = filteredElevations;
                _filteredElevations = filteredElevations;
            }
        }

        /// <summary>
        /// Handles checkbox click events for elevations
        /// </summary>
        private void ElevationCheckbox_Click(object sender, RoutedEventArgs e)
        {
            // Get the clicked checkbox
            System.Windows.Controls.CheckBox checkBox = sender as System.Windows.Controls.CheckBox;
            if (checkBox == null) return;

            // Get the elevation view model associated with this checkbox
            ElevationViewModel currentElevation = checkBox.DataContext as ElevationViewModel;
            if (currentElevation == null) return;

            // Check if Shift key is pressed for batch selection
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                if (_lastSelectedElevation != null)
                {
                    // Determine the range of indices to select
                    int startIndex = Math.Min(_lastSelectedElevation.Index, currentElevation.Index);
                    int endIndex = Math.Max(_lastSelectedElevation.Index, currentElevation.Index);

                    // Find all elevation objects within this range in the current filtered collection
                    var elevationsInRange = _filteredElevations.Where(r =>
                                                    r.Index >= startIndex &&
                                                    r.Index <= endIndex).ToList();

                    // Set all elevations in the range to have the same IsSelected value as the current elevation
                    foreach (var elevation in elevationsInRange)
                    {
                        elevation.IsSelected = currentElevation.IsSelected;
                    }
                }
            }

            // Store this as the last selected elevation for future shift-click operations
            _lastSelectedElevation = currentElevation;
        }

        /// <summary>
        /// Selects all elevations in the current filtered view
        /// </summary>
        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (ElevationViewModel elevation in _filteredElevations)
            {
                elevation.IsSelected = true;
            }
        }

        /// <summary>
        /// Clears selection for all elevations in the current filtered view
        /// </summary>
        private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (ElevationViewModel elevation in _filteredElevations)
            {
                elevation.IsSelected = false;
            }
        }

        /// <summary>
        /// Handler for Next button click
        /// </summary>
        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            // Collect selected elevations
            SelectedElevations = new List<ElevationViewModel>();

            foreach (ElevationViewModel elevation in _elevations)
            {
                if (elevation.IsSelected)
                {
                    SelectedElevations.Add(elevation);
                }
            }

            // Validate selection
            if (SelectedElevations.Count == 0)
            {
                TaskDialog.Show("Selection Required", "Please select at least one elevation to create reference plans.");
                return;
            }

            // Open the plan configuration window
            PlanConfigurationWindow configWindow = new PlanConfigurationWindow(_uiDoc, SelectedElevations);
            bool? result = configWindow.ShowDialog();

            if (result == true)
            {
                // Close this window if the configuration window was successful
                DialogResult = true;
                Close();
            }
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
}