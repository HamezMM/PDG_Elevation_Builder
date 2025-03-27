using System.ComponentModel;
using Autodesk.Revit.DB;

namespace PDG_Elevation_Builder
{
    /// <summary>
    /// View model for elevation views in the selection list
    /// </summary>
    public class ElevationViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public ElementId Id { get; set; }
        public string Name { get; set; }
        public string ViewType { get; set; } // e.g., "Interior Elevation", "Exterior Elevation"
        public string Scale { get; set; }
        public string Level { get; set; }
        public int Index { get; set; } // For shift-selection support

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
}