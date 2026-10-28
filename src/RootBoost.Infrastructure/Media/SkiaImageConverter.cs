using RootBoost.Application.Media;
using SkiaSharp;

namespace RootBoost.Infrastructure.Media;

/// <summary>
/// Foto de qualquer formato que o Skia lê vira JPEG: gira conforme o EXIF (foto de celular), reduz o lado maior e põe
/// fundo branco onde era transparente (PNG). SkiaSharp é MIT e roda no contêiner Linux sem dependência nativa extra.
/// </summary>
public sealed class SkiaImageConverter : IImageConverter
{
    public byte[]? ToJpeg(byte[] image, int maxSide)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(image));
        if (codec is null) return null;
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0) return null;

        var origin = codec.EncodedOrigin;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int srcW = swap ? bitmap.Height : bitmap.Width, srcH = swap ? bitmap.Width : bitmap.Height;
        var scale = Math.Min(1f, (float)maxSide / Math.Max(srcW, srcH));
        int w = Math.Max(1, (int)Math.Round(srcW * scale)), h = Math.Max(1, (int)Math.Round(srcH * scale));

        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Scale(scale);
        Orient(canvas, origin, bitmap.Width, bitmap.Height);
        using (var img = SKImage.FromBitmap(bitmap))
            canvas.DrawImage(img, 0, 0, new SKSamplingOptions(SKCubicResampler.Mitchell));
        canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data?.ToArray();
    }

    /// <summary>Leva o desenho da orientação gravada no arquivo pra "de pé" (as 8 orientações do EXIF).</summary>
    private static void Orient(SKCanvas c, SKEncodedOrigin origin, int w, int h)
    {
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: c.Translate(w, 0); c.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight: c.Translate(w, h); c.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft: c.Translate(0, h); c.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop: c.RotateDegrees(90); c.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: c.Translate(h, 0); c.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: c.Translate(h, w); c.RotateDegrees(90); c.Scale(-1, 1); break;
            case SKEncodedOrigin.LeftBottom: c.Translate(0, w); c.RotateDegrees(270); break;
        }
    }
}
