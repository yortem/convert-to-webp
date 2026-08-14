using ImageMagick;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinForms = System.Windows.Forms;

namespace ConvertToWebP
{
    public partial class MainWindow : System.Windows.Window
    {
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".tif", ".webp" };

        private AppSettings _settings = new AppSettings();
        private CancellationTokenSource? _cts;
        private readonly HashSet<string> _queuedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

            int index = CompressionEffortComboBox.Items.IndexOf(CompressionEffortComboBox.Items.Cast<object>().FirstOrDefault(i => (int)((ComboBoxItem)i).Tag == _settings.CompressionMethod));
            if (index >= 0) CompressionEffortComboBox.SelectedIndex = index;

            if (_settings.UseCustomOutput)
            {
                RadioCustomFolder.IsChecked = true;
                CustomPathTextBox.Text = _settings.CustomOutputPath;
            }
            else
            {
                RadioSameFolder.IsChecked = true;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _cts?.Cancel();

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
            if (CompressionEffortComboBox.SelectedItem is ComboBoxItem selected)
            {
                _settings.CompressionMethod = (int)selected.Tag;
            }
            _settings.Save();
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
                        var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Where(IsImageFile);
                        foreach (var file in files) AddToLog(file);
                    }
                    catch { }
                }
            }
        }

        private void AddToLog(string filePath)
        {
            if (!_queuedPaths.Add(filePath)) return;
            var fi = new FileInfo(filePath);
            LogEntries.Add(new LogEntry
            {
                FullPath = filePath,
                Filename = Path.GetFileName(filePath),
                OriginalBytes = fi.Length,
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
                    var firstItem = LogEntries.FirstOrDefault(x => x.Status.Contains("Saved") || x.Status == "Pending");
                    if (firstItem != null)
                    {
                        string dir = Path.GetDirectoryName(firstItem.FullPath) ?? "";
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
            _queuedPaths.Clear();
            TotalSavingsText.Text = "0 MB";
        }

        private void RemoveSelectedItems()
        {
            var selectedItems = LogListView.SelectedItems.Cast<LogEntry>().ToList();
            foreach (var item in selectedItems)
            {
                _queuedPaths.Remove(item.FullPath);
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

            if (RadioCustomFolder.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(CustomPathTextBox.Text) || !Directory.Exists(CustomPathTextBox.Text))
                {
                    System.Windows.MessageBox.Show("Please select a valid custom output folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            int quality = (int)QualitySlider.Value;
            bool resize = ResizeCheckBox.IsChecked == true;
            int maxWidth = 1600;
            int.TryParse(MaxWidthTextBox.Text, out maxWidth);
            bool prefix = PrefixCheckBox.IsChecked == true;
            bool stripMetadata = StripMetadataCheckBox.IsChecked == true;
            bool useCustom = RadioCustomFolder.IsChecked == true;
            string customPath = CustomPathTextBox.Text;
            int compressionMethod = CompressionEffortComboBox.SelectedItem is ComboBoxItem item ? (int)item.Tag : 4;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            SetBusy(true);

            try
            {
                await Task.Run(() =>
                {
                    Parallel.ForEach(pendingItems,
                        new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) },
                        item => ConvertFile(item, quality, resize, maxWidth, prefix, stripMetadata, useCustom, customPath, compressionMethod, token));
                });
            }
            catch (OperationCanceledException)
            {
                // user closed the window; nothing to report
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetBusy(false);
            }

            TotalSavingsText.Text = FormatBytes(LogEntries.Where(x => x.NewBytes > 0).Sum(x => x.OriginalBytes - x.NewBytes));
        }

        private bool IsImageFile(string path) => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        private void SetBusy(bool busy)
        {
            CompressButton.IsEnabled = !busy;
            ClearListButton.IsEnabled = !busy;
            OpenFolderButton.IsEnabled = !busy;
        }

        private void ConvertFile(LogEntry item, int quality, bool resize, int maxWidth, bool prefix, bool stripMetadata, bool useCustom, string customPath, int compressionMethod, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
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
                    string folder = Path.GetDirectoryName(filePath) ?? Path.GetTempPath();
                    exportFolder = Path.Combine(folder, "WebP_Export");
                }

                Directory.CreateDirectory(exportFolder);

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
                    image.Settings.SetDefine(MagickFormat.WebP, "method", compressionMethod.ToString());

                    token.ThrowIfCancellationRequested();
                    image.Write(outputPath);
                }

                var newFileInfo = new FileInfo(outputPath);
                long newSize = newFileInfo.Length;

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    item.NewBytes = newSize;
                    item.NewSize = FormatBytes(newSize);
                    item.Status = $"Saved {((originalSize - newSize) / (double)originalSize):P0}";
                });
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) return;
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
        public string FullPath { get; set; } = "";
        public string Filename { get; set; } = "";
        public string OriginalSize { get; set; } = "";
        public long OriginalBytes { get; set; }
        public long NewBytes { get; set; }

        private string _newSize = "-";
        public string NewSize
        {
            get => _newSize;
            set { _newSize = value; OnPropertyChanged("NewSize"); }
        }

        private string _status = "Pending";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged("Status"); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}
