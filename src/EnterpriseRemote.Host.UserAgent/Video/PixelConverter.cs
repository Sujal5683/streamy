using System;

using System.Threading.Tasks;

namespace EnterpriseRemote.Host.UserAgent.Video;

public static class PixelConverter

{

    /// <summary>

    /// Converts a 32-bit BGRA buffer to NV12 format using multi-threaded row processing.

    /// NV12 format: Plane 0 is Y (Width * Height), Plane 1 is interleaved UV (Width * Height / 2).

    /// Total buffer size: Width * Height * 1.5 bytes.

    /// </summary>

    public static unsafe void BgraToNv12(byte[] bgra, int width, int height, byte[] nv12)

    {

        int yPlaneSize = width * height;

        int uvPlaneOffset = yPlaneSize;

        fixed (byte* pBgra = bgra)

        fixed (byte* pNv12 = nv12)

        {

            byte* rawBgra = pBgra;

            byte* rawNv12 = pNv12;

            Parallel.For(0, height / 2, rowPair =>

            {

                int y1 = rowPair * 2;

                int y2 = y1 + 1;

                int bgraRow1 = y1 * width * 4;

                int bgraRow2 = y2 * width * 4;

                int yPlaneRow1 = y1 * width;

                int yPlaneRow2 = y2 * width;

                int uvPlaneRow = uvPlaneOffset + (rowPair * width);

                for (int x = 0; x < width; x += 2)

                {

                    // Pixel (x, y1)

                    int p1 = bgraRow1 + (x * 4);

                    byte b1 = rawBgra[p1 + 0];

                    byte g1 = rawBgra[p1 + 1];

                    byte r1 = rawBgra[p1 + 2];

                    rawNv12[yPlaneRow1 + x] = RgbToY(r1, g1, b1);

                    // Pixel (x+1, y1)

                    int p2 = bgraRow1 + ((x + 1) * 4);

                    byte b2 = rawBgra[p2 + 0];

                    byte g2 = rawBgra[p2 + 1];

                    byte r2 = rawBgra[p2 + 2];

                    rawNv12[yPlaneRow1 + x + 1] = RgbToY(r2, g2, b2);

                    // Pixel (x, y2)

                    int p3 = bgraRow2 + (x * 4);

                    byte b3 = rawBgra[p3 + 0];

                    byte g3 = rawBgra[p3 + 1];

                    byte r3 = rawBgra[p3 + 2];

                    rawNv12[yPlaneRow2 + x] = RgbToY(r3, g3, b3);

                    // Pixel (x+1, y2)

                    int p4 = bgraRow2 + ((x + 1) * 4);

                    byte b4 = rawBgra[p4 + 0];

                    byte g4 = rawBgra[p4 + 1];

                    byte r4 = rawBgra[p4 + 2];

                    rawNv12[yPlaneRow2 + x + 1] = RgbToY(r4, g4, b4);

                    // Subsampled 2x2 average for U and V

                    int avgR = (r1 + r2 + r3 + r4) >> 2;

                    int avgG = (g1 + g2 + g3 + g4) >> 2;

                    int avgB = (b1 + b2 + b3 + b4) >> 2;

                    rawNv12[uvPlaneRow + x + 0] = RgbToU(avgR, avgG, avgB);

                    rawNv12[uvPlaneRow + x + 1] = RgbToV(avgR, avgG, avgB);

                }

            });

        }

    }

    /// <summary>

    /// Downscales a BGRA32 source buffer to a smaller destination buffer using box-filter averaging.

    /// Each destination pixel is the average of the corresponding (scaleX × scaleY) source pixels.

    /// The scale factors are computed from (srcW/dstW) and (srcH/dstH); they must be integer multiples.

    /// All four BGRA channels are averaged independently.

    /// </summary>

    /// <param name="src">Source pixel data in BGRA32 format (4 bytes per pixel).</param>

    /// <param name="srcW">Width of the source image in pixels.</param>

    /// <param name="srcH">Height of the source image in pixels.</param>

    /// <param name="dst">Destination pixel data buffer (must be at least dstW * dstH * 4 bytes).</param>

    /// <param name="dstW">Width of the destination image in pixels.</param>

    /// <param name="dstH">Height of the destination image in pixels.</param>

    public static void FastBoxDownscaleBgra32(byte[] src, int srcW, int srcH, byte[] dst, int dstW, int dstH)
    {
        if (src == null) throw new ArgumentNullException(nameof(src));
        if (dst == null) throw new ArgumentNullException(nameof(dst));
        if (dstW <= 0 || dstH <= 0) throw new ArgumentOutOfRangeException(nameof(dstW), "Destination dimensions must be positive.");
        if (dst.Length < dstW * dstH * 4) throw new ArgumentException("Destination buffer is too small.", nameof(dst));

        int maxSrcX = srcW - 1;
        int maxSrcY = srcH - 1;

        Parallel.For(0, dstH, dy =>
        {
            int srcY0 = (dy * srcH) / dstH;
            int srcY1 = Math.Min(srcY0 + 1, maxSrcY);
            int srcRow0 = srcY0 * srcW * 4;
            int srcRow1 = srcY1 * srcW * 4;
            int dstRowOffset = dy * dstW * 4;

            for (int dx = 0; dx < dstW; dx++)
            {
                int srcX0 = (dx * srcW) / dstW;
                int srcX1 = Math.Min(srcX0 + 1, maxSrcX);

                int idx00 = srcRow0 + (srcX0 * 4);
                int idx10 = srcRow0 + (srcX1 * 4);
                int idx01 = srcRow1 + (srcX0 * 4);
                int idx11 = srcRow1 + (srcX1 * 4);

                int dstIdx = dstRowOffset + (dx * 4);

                // 2x2 box bilinear area filtering for B, G, R, A (crystal-clear text and UI borders)
                dst[dstIdx + 0] = (byte)((src[idx00 + 0] + src[idx10 + 0] + src[idx01 + 0] + src[idx11 + 0] + 2) >> 2);
                dst[dstIdx + 1] = (byte)((src[idx00 + 1] + src[idx10 + 1] + src[idx01 + 1] + src[idx11 + 1] + 2) >> 2);
                dst[dstIdx + 2] = (byte)((src[idx00 + 2] + src[idx10 + 2] + src[idx01 + 2] + src[idx11 + 2] + 2) >> 2);
                dst[dstIdx + 3] = (byte)((src[idx00 + 3] + src[idx10 + 3] + src[idx01 + 3] + src[idx11 + 3] + 2) >> 2);
            }
        });
    }

    private static byte RgbToY(int r, int g, int b)

    {

        int y = ((66 * r + 129 * g + 25 * b + 128) >> 8) + 16;

        return (byte)Math.Clamp(y, 16, 235);

    }

    private static byte RgbToU(int r, int g, int b)

    {

        int u = ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;

        return (byte)Math.Clamp(u, 16, 240);

    }

    private static byte RgbToV(int r, int g, int b)

    {

        int v = ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;

        return (byte)Math.Clamp(v, 16, 240);

    }

}

