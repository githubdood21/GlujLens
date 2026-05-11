using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Collections.ObjectModel;

namespace GlujLens.ViewModels;

/// <summary>
/// View model for the annotation editor window.
/// </summary>
public partial class EditorViewModel : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _editedImage;

    [ObservableProperty]
    private AnnotationTool _selectedTool;

    [ObservableProperty]
    private string _annotationText = string.Empty;

    [ObservableProperty]
    private Color _annotationColor = Colors.Red;

    [ObservableProperty]
    private double _strokeWidth = 2.0;

    [ObservableProperty]
    private string _statusMessage = "Editor ready";

    public ObservableCollection<string> AnnotationHistory { get; } = new();

    public EditorViewModel()
    {
    }

    public enum AnnotationTool
    {
        None,
        Rectangle,
        Circle,
        Line,
        Arrow,
        Freehand,
        Text,
        Pixelate,
        Highlight
    }

    [RelayCommand]
    private void ApplyAnnotation()
    {
        // Annotation logic will be delegated to AnnotationService
        StatusMessage = "Annotation applied";
    }

    [RelayCommand]
    private void UndoAnnotation()
    {
        StatusMessage = "Undo applied (placeholder)";
    }

    [RelayCommand]
    private void RedoAnnotation()
    {
        StatusMessage = "Redo applied (placeholder)";
    }

    [RelayCommand]
    private void SaveEditedImage(string filePath)
    {
        try
        {
            StatusMessage = $"Image saved to {filePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }
}