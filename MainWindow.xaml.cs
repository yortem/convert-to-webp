using ImageMagick;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinForms = System.Windows.Forms;

namespace ConvertToWebP
{
    public partial class MainWindow : System.Windows.Window
    {
        private AppSettings _settings;
        public ObservableCollection<LogEntry> LogEntries { get; set; } = new ObservableCollection<LogEntry>();

        public MainWindow()
        {
            InitializeComponent();
            LogListView.ItemsSource = LogEntries;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _settings = AppSettings.Load();
            QualitySlider.Value = _settings.Quality;
            ResizeCheckBox.IsChecked = _settings.ResizeEnabled;
            MaxWidthTextBox.Text = _settings.MaxWidth.ToString();
            PrefixCheckBox.IsChecked = _settings.AddPrefix;
            StripMetadataCheckBox.IsChecked = _settings.StripMetadata;

            if (_settings.UseCustomOutput)
            {
                RadioCustomFolder.IsChecked = true;
                CustomPathTextBox.Text = _settings.CustomOutputPath;
            }
            else
            {
                RadioSameFolder.IsChecked = true;
            }

            UpdateUIState();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _settings.Quality = (int)QualitySlider.Value;
            _settings.ResizeEnabled = ResizeCheckBox.IsChecked == true;
            if (int.TryParse(MaxWidthTextBox.Text, out int maxWidth))
            {
                _settings.MaxWidth = maxWidth;
            }
            _settings.AddPrefix = PrefixCheckBox.IsChecked == true;
            _settings.StripMetadata = StripMetadataCheckBox.IsChecked == true;
            _settings.UseCustomOutput = RadioCustomFolder.IsChecked == true;
            _settings.CustomOutputPath = CustomPathTextBox.Text;
            _settings.Save();
        }

        private void ResizeCheckBox_Click(object sender, RoutedEventArgs e) => UpdateUIState();
        private void RadioSameFolder_Checked(object sender, RoutedEventArgs e) => UpdateUIState();
        private void RadioCustomFolder_Checked(object sender, RoutedEventArgs e) => UpdateUIState();

        private void UpdateUIState()
        {
            // Logic handled by bindings mostly, but explicit updates here if needed
        }

        private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) e.Effects = System.Windows.DragDropEffects.Copy;
            else e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                QueueFiles(files);
            }
        }

        private void QueueFiles(string[] paths)
        {
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    if (IsImageFile(path)) AddToLog(path);
                }
                else if (Directory.Exists(path))
                {
                    try
                    {
                        var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).Where(IsImageFile);
                        foreach (var file in files) AddToLog(file);
                    }
                    catch { }
                }
            }
        }

        private void AddToLog(string filePath)
        {
            if (LogEntries.Any(x => x.FullPath == filePath)) return;
            var fi = new FileInfo(filePath);
            LogEntries.Add(new LogEntry
            {
                FullPath = filePath,
                Filename = Path.GetFileName(filePath),
                OriginalSize = FormatBytes(fi.Length),
                NewSize = "-",
                Status = "Pending"
            });
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new WinForms.FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    CustomPathTextBox.Text = dialog.SelectedPath;
                    RadioCustomFolder.IsChecked = true;
                }
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = "";
                if (RadioCustomFolder.IsChecked == true && Directory.Exists(CustomPathTextBox.Text))
                {
                    path = CustomPathTextBox.Text;
                }
                else if (LogEntries.Count > 0)
                {
                    // Try to open the folder of the first item, or its WebP_Export subfolder
                    var firstItem = LogEntries.FirstOrDefault(x => x.Status.Contains("Saved") || x.Status == "Pending");
                    if (firstItem != null)
                    {
                        string dir = Path.GetDirectoryName(firstItem.FullPath);
                        if (RadioSameFolder.IsChecked == true)
                        {
                            string exportDir = Path.Combine(dir, "WebP_Export");
                            if (Directory.Exists(exportDir)) path = exportDir;
                            else path = dir;
                        }
                        else
                        {
                            path = dir;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", path);
                }
                else
                {
                    System.Windows.MessageBox.Show("Cannot determine folder to open (no custom path or no images in list).", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error opening folder: {ex.Message}");
            }
        }

        private void LogListView_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                RemoveSelectedItems();
            }
        }

        private void RemoveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedItems();
        }

        private void ClearListButton_Click(object sender, RoutedEventArgs e)
        {
            LogEntries.Clear();
            TotalSavingsText.Text = "0 MB";
        }

        private void RemoveSelectedItems()
        {
            var selectedItems = LogListView.SelectedItems.Cast<LogEntry>().ToList();
            foreach (var item in selectedItems)
            {
                LogEntries.Remove(item);
            }
        }

        private async void CompressButton_Click(object sender, RoutedEventArgs e)
        {
            var pendingItems = LogEntries.Where(x => x.Status == "Pending").ToList();
            if (pendingItems.Count == 0)
            {
                System.Windows.MessageBox.Show("No pending images to compress.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Validate Custom Path
            if (RadioCustomFolder.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(CustomPathTextBox.Text) || !Directory.Exists(CustomPathTextBox.Text))
                {
                    System.Windows.MessageBox.Show("Please select a valid custom output folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            CompressButton.IsEnabled = false;

            int quality = (int)QualitySlider.Value;
            bool resize = ResizeCheckBox.IsChecked == true;
            int maxWidth = 1600;
            int.TryParse(MaxWidthTextBox.Text, out maxWidth);
            bool prefix = PrefixCheckBox.IsChecked == true;
            bool stripMetadata = StripMetadataCheckBox.IsChecked == true;
            bool useCustom = RadioCustomFolder.IsChecked == true;
            string customPath = CustomPathTextBox.Text;

            await Task.Run(() =>
            {
                foreach (var item in pendingItems)
                {
                    ConvertFile(item, quality, resize, maxWidth, prefix, stripMetadata, useCustom, customPath);
                }
            });

            CompressButton.IsEnabled = true;
            TotalSavingsText.Text = "Batch Completed";
        }

        private bool IsImageFile(string path)
        {
            var ext = Path.GetExtension(path).ToLower();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".tiff" || ext == ".tif" || ext == ".webp";
        }

        private void ConvertFile(LogEntry item, int quality, bool resize, int maxWidth, bool prefix, bool stripMetadata, bool useCustom, string customPath)
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => item.Status = "Processing...");

                string filePath = item.FullPath;
                var fileInfo = new FileInfo(filePath);
                long originalSize = fileInfo.Length;

                string exportFolder;
                if (useCustom)
                {
                    exportFolder = customPath;
                }
                else
                {
                    string folder = Path.GetDirectoryName(filePath);
                    exportFolder = Path.Combine(folder, "WebP_Export");
                }

                if (!Directory.Exists(exportFolder)) Directory.CreateDirectory(exportFolder);

                string fileNameNoExt = Path.GetFileNameWithoutExtension(filePath);
                string newFileName = prefix ? $"compressed_{fileNameNoExt}.webp" : $"{fileNameNoExt}.webp";
                string outputPath = Path.Combine(exportFolder, newFileName);

                using (var image = new MagickImage(filePath))
                {
                    if (stripMetadata)
                    {
                        image.Strip();
                    }

                    if (resize && (image.Width > maxWidth || image.Height > maxWidth))
                    {
                        image.Resize(new MagickGeometry($"{maxWidth}x{maxWidth}>"));
                    }

                    image.Format = MagickFormat.WebP;
                    image.Quality = (uint)quality;
                    image.Settings.SetDefine(MagickFormat.WebP, "method", "6");

                    image.Write(outputPath);
                }

                var newFileInfo = new FileInfo(outputPath);
                long newSize = newFileInfo.Length;

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    item.NewSize = FormatBytes(newSize);
                    item.Status = $"Saved {((originalSize - newSize) / (double)originalSize):P0}";
                });
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    item.Status = "Error: " + ex.Message;
                });
            }
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }

    public class LogEntry : System.ComponentModel.INotifyPropertyChanged
    {
        public string FullPath { get; set; }
        public string Filename { get; set; }
        public string OriginalSize { get; set; }

        private string _newSize;
        public string NewSize
        {
            get => _newSize;
            set { _newSize = value; OnPropertyChanged("NewSize"); }
        }

        private string _status;
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged("Status"); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}