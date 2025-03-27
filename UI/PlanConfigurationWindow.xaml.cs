using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PDG_Elevation_Builder.Models;

namespace PDG_Elevation_Builder.UI
{
    /// <summary>
    /// Interaction logic for PlanConfigurationWindow.xaml
    /// </summary>
    public partial class PlanConfigurationWindow : Window
    {
        private UIDocument _uiDoc;
        private Document _doc;
        private ObservableCollection<ReferencePlanConfiguration> _configurations;
        private ReferencePlanConfiguration _selectedConfig;

        public List<ReferencePlanConfiguration> Configurations { get; private set; }

        /// <summary>
        /// Constructor for the Plan Configuration Window
        /// </summary>
        /// <param name="uiDoc">Current Revit UIDocument</param>
        /// <param name="selectedElevations">List of selected elevation view models</param>
        public PlanConfigurationWindow(UIDocument uiDoc, List<ElevationViewModel> selectedElevations)
        {
            InitializeComponent();

            _uiDoc = uiDoc;
            _doc = uiDoc.Document;

            // Create configuration objects for each selected elevation
            _configurations = new ObservableCollection<ReferencePlanConfiguration>();

            foreach (var elevation in selectedElevations)
            {
                _configurations.Add(new ReferencePlanConfiguration(elevation.Id, elevation.Name));
            }

            // Set as data source for the elevations list
            ElevationsListBox.ItemsSource = _configurations;

            // Select the first item by default
            if (_configurations.Count > 0)
            {
                ElevationsListBox.SelectedIndex = 0;
                _selectedConfig = _configurations[0];
                UpdateConfigPanel();
            }

            // Set window owner to Revit's main window for proper modal behavior
            Owner = System.Windows.Interop.HwndSource.FromHwnd(
                Autodesk.Windows.ComponentManager.ApplicationWindow).RootVisual as Window;
        }

        /// <summary>
        /// Updates the configuration panel with the selected elevation's settings
        /// </summary>
        private void UpdateConfigPanel()
        {
            if (_selectedConfig == null)
            {
                ConfigPanel.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            ConfigPanel.Visibility = System.Windows.Visibility.Visible;
            ElevationNameTextBlock.Text = _selectedConfig.ElevationName;

            // Update the cut heights list
            UpdateCutHeightsList();

            // Set the plan extent value
            PlanExtentTextBox.Text = _selectedConfig.PlanExtent.ToString("0.00");
        }

        /// <summary>
        /// Updates the cut heights list with the current configuration
        /// </summary>
        private void UpdateCutHeightsList()
        {
            CutHeightsListBox.Items.Clear();

            foreach (double height in _selectedConfig.CutHeights)
            {
                CutHeightsListBox.Items.Add($"{height:0.00}' above level");
            }
        }

        /// <summary>
        /// Handler for elevation selection changed
        /// </summary>
        private void ElevationsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedConfig = ElevationsListBox.SelectedItem as ReferencePlanConfiguration;
            UpdateConfigPanel();
        }

        /// <summary>
        /// Handler for add cut height button
        /// </summary>
        private void AddCutHeightButton_Click(object sender, RoutedEventArgs e)
        {
            // Default to 4' or increment from highest existing height
            double newHeight = 4.0;

            if (_selectedConfig.CutHeights.Count > 0)
            {
                newHeight = _selectedConfig.CutHeights.Max() + 2.0; // Add 2' to highest existing height
            }

            _selectedConfig.CutHeights.Add(newHeight);
            UpdateCutHeightsList();
        }

        /// <summary>
        /// Handler for edit cut height button
        /// </summary>
        private void EditCutHeightButton_Click(object sender, RoutedEventArgs e)
        {
            int selectedIndex = CutHeightsListBox.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < _selectedConfig.CutHeights.Count)
            {
                // Show a dialog to edit the value
                string currentValue = _selectedConfig.CutHeights[selectedIndex].ToString("0.00");

                // Create a simple dialog
                Window editDialog = new Window
                {
                    Title = "Edit Cut Height",
                    Width = 300,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize
                };

                // Create content
                StackPanel panel = new StackPanel { Margin = new Thickness(10) };
                panel.Children.Add(new TextBlock { Text = "Height (feet):" });
                System.Windows.Controls.TextBox heightTextBox = new System.Windows.Controls.TextBox { Text = currentValue, Margin = new Thickness(0, 5, 0, 10) };
                panel.Children.Add(heightTextBox);

                // Add buttons
                StackPanel buttonPanel = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                };

                System.Windows.Controls.Button okButton = new System.Windows.Controls.Button
                {
                    Content = "OK",
                    Width = 75,
                    Height = 25,
                    Margin = new Thickness(0, 0, 10, 0)
                };

                System.Windows.Controls.Button cancelButton = new System.Windows.Controls.Button
                {
                    Content = "Cancel",
                    Width = 75,
                    Height = 25
                };

                buttonPanel.Children.Add(okButton);
                buttonPanel.Children.Add(cancelButton);
                panel.Children.Add(buttonPanel);
                editDialog.Content = panel;

                // Handle button clicks
                okButton.Click += (s, args) =>
                {
                    if (double.TryParse(heightTextBox.Text, out double newHeight))
                    {
                        // Validate height (must be positive)
                        if (newHeight <= 0)
                        {
                            System.Windows.MessageBox.Show("Height must be greater than zero.", "Invalid Height",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        _selectedConfig.CutHeights[selectedIndex] = newHeight;
                        UpdateCutHeightsList();
                        editDialog.DialogResult = true;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Please enter a valid number.", "Invalid Input",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                };

                cancelButton.Click += (s, args) =>
                {
                    editDialog.DialogResult = false;
                };

                // Show the dialog
                editDialog.ShowDialog();
            }
        }

        /// <summary>
        /// Handler for remove cut height button
        /// </summary>
        private void RemoveCutHeightButton_Click(object sender, RoutedEventArgs e)
        {
            int selectedIndex = CutHeightsListBox.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < _selectedConfig.CutHeights.Count)
            {
                // Don't allow removing the last cut height
                if (_selectedConfig.CutHeights.Count <= 1)
                {
                    System.Windows.MessageBox.Show("At least one cut height is required.", "Cannot Remove",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _selectedConfig.CutHeights.RemoveAt(selectedIndex);
                UpdateCutHeightsList();
            }
        }

        /// <summary>
        /// Handler for plan extent text box changed
        /// </summary>
        private void PlanExtentTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_selectedConfig != null && double.TryParse(PlanExtentTextBox.Text, out double extent))
            {
                _selectedConfig.PlanExtent = extent;
            }
        }

        /// <summary>
        /// Handler for apply to all button
        /// </summary>
        private void ApplyToAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedConfig == null) return;

            // Get values from current configuration
            double planExtent = _selectedConfig.PlanExtent;
            List<double> cutHeights = new List<double>(_selectedConfig.CutHeights);

            // Apply to all configurations
            foreach (var config in _configurations)
            {
                if (config != _selectedConfig) // Skip the current configuration
                {
                    config.PlanExtent = planExtent;
                    config.CutHeights.Clear();
                    config.CutHeights.AddRange(cutHeights);
                }
            }

            System.Windows.MessageBox.Show("Settings applied to all elevations.", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Handler for create button click
        /// </summary>
        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate all configurations
            foreach (var config in _configurations)
            {
                if (config.CutHeights.Count == 0)
                {
                    System.Windows.MessageBox.Show($"No cut heights defined for {config.ElevationName}.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (config.PlanExtent <= 0)
                {
                    System.Windows.MessageBox.Show($"Invalid plan extent for {config.ElevationName}.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // Store configurations for use by generator
            Configurations = new List<ReferencePlanConfiguration>(_configurations);

            // Create reference plans
            try
            {
                var generator = new ReferencePlanGenerator(_doc);
                using (Transaction trans = new Transaction(_doc, "Create Reference Plans"))
                {
                    trans.Start();
                    var result = generator.CreateReferencePlans(Configurations);
                    trans.Commit();

                    // Show success message with summary
                    TaskDialog.Show("Reference Plan Creation Complete",
                        $"Successfully created {result.Count} reference plans for {Configurations.Count} elevations.");
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error",
                    $"An error occurred while creating reference plans:\n\n{ex.Message}");
            }
        }

        /// <summary>
        /// Handler for cancel button click
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}