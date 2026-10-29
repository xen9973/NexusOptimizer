using System.Collections.Generic;
using System.Windows;

namespace NexusOptimizer.Views;

/// <summary>Строка списка файлов в окне просмотра очистки.</summary>
public sealed record CleanupPreviewRow(string Path, string SizeText);

public partial class CleanupPreviewWindow : Window
{
    public CleanupPreviewWindow(string title, string summary, IReadOnlyList<CleanupPreviewRow> rows)
    {
        InitializeComponent();

        Title = "Файлы для очистки: " + title;
        TitleText.Text = title;
        SummaryText.Text = summary;
        RowsList.ItemsSource = rows;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
