namespace CaptureOk.Core.Geometry;

/// <summary>모니터 하나. Bounds는 물리 픽셀, Scale은 DPI/96.</summary>
public sealed record MonitorInfo(PixelRect Bounds, double Scale, bool IsPrimary);
