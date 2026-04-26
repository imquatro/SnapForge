using System;
using System.Windows.Media.Imaging;

namespace SnapForge.Models;

public sealed class ScreenshotItem
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required BitmapImage Thumbnail { get; init; }
}
