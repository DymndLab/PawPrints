namespace Blueprint.Rendering;

static class ImageResampling
{
    // Premultiplied interpolation avoids dark/coloured fringes at transparent edges.
    public static byte[] ResizeRgba(byte[] source, int width, int height, int targetWidth, int targetHeight)
    {
        Validate(source, width, height, targetWidth, targetHeight, 4);
        if (width == targetWidth && height == targetHeight)
            return source;
        var result = new byte[checked(targetWidth * targetHeight * 4)];
        for (int y = 0; y < targetHeight; y++)
            for (int x = 0; x < targetWidth; x++)
            {
                double sx = Math.Clamp((x + .5) * width / targetWidth - .5, 0, width - 1);
                double sy = Math.Clamp((y + .5) * height / targetHeight - .5, 0, height - 1);
                int ix = (int)sx, iy = (int)sy, output = (y * targetWidth + x) * 4;
                double fx = sx - ix, fy = sy - iy, alpha = 0, red = 0, green = 0, blue = 0;
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        int input = (Math.Min(iy + j, height - 1) * width + Math.Min(ix + i, width - 1)) * 4;
                        double weight = (i == 0 ? 1 - fx : fx) * (j == 0 ? 1 - fy : fy) * source[input + 3];
                        alpha += weight;
                        red += source[input] * weight;
                        green += source[input + 1] * weight;
                        blue += source[input + 2] * weight;
                    }
                if (alpha > 0)
                {
                    result[output] = ToByte(red / alpha);
                    result[output + 1] = ToByte(green / alpha);
                    result[output + 2] = ToByte(blue / alpha);
                    result[output + 3] = ToByte(alpha);
                }
            }
        return result;
    }

    // Separable Mitchell-Netravali filter (B=C=1/3). Widen the kernel when
    // reducing an image so thin drawing lines are averaged rather than skipped.
    public static byte[] ResizeRgb(byte[] source, int width, int height, int targetWidth, int targetHeight, CancellationToken token = default)
    {
        Validate(source, width, height, targetWidth, targetHeight, 3);
        if (width == targetWidth && height == targetHeight)
            return source;
        var horizontal = Contributions(width, targetWidth);
        var vertical = Contributions(height, targetHeight);
        var result = new byte[checked(targetWidth * targetHeight * 3)];
        // Keep only rows needed by this kernel, not a full float-sized image.
        var rows = new Dictionary<int, float[]>();
        var reusable = new Stack<float[]>();
        var activeRows = new float[vertical.Max(k => k.Weights.Length)][];
        for (int y = 0; y < targetHeight; y++)
        {
            token.ThrowIfCancellationRequested();
            var kernel = vertical[y];
            foreach (int old in rows.Keys.Where(row => row < kernel.Start).ToArray())
            {
                reusable.Push(rows[old]);
                rows.Remove(old);
            }
            for (int k = 0; k < kernel.Weights.Length; k++)
            {
                int sy = kernel.Start + k;
                if (rows.ContainsKey(sy))
                    continue;
                var row = reusable.Count > 0 ? reusable.Pop() : new float[targetWidth * 3];
                for (int x = 0; x < targetWidth; x++)
                    for (int channel = 0; channel < 3; channel++)
                    {
                        double value = 0;
                        var h = horizontal[x];
                        for (int i = 0; i < h.Weights.Length; i++)
                            value += source[(sy * width + h.Start + i) * 3 + channel] * h.Weights[i];
                        row[x * 3 + channel] = (float)value;
                    }
                rows.Add(sy, row);
            }
            for (int k = 0; k < kernel.Weights.Length; k++)
                activeRows[k] = rows[kernel.Start + k];
            for (int x = 0; x < targetWidth * 3; x++)
            {
                double value = 0;
                for (int k = 0; k < kernel.Weights.Length; k++)
                    value += activeRows[k][x] * kernel.Weights[k];
                result[y * targetWidth * 3 + x] = ToByte(value);
            }
        }
        return result;
    }

    sealed record Kernel(int Start, double[] Weights);

    static Kernel[] Contributions(int source, int target)
    {
        var result = new Kernel[target];
        double scale = Math.Max(1, (double)source / target);
        for (int i = 0; i < target; i++)
        {
            double centre = (i + .5) * source / target - .5;
            int start = Math.Max(0, (int)Math.Ceiling(centre - 2 * scale));
            int end = Math.Min(source - 1, (int)Math.Floor(centre + 2 * scale));
            var weights = new double[end - start + 1];
            double sum = 0;
            for (int j = start; j <= end; j++)
                sum += weights[j - start] = Mitchell(Math.Abs(j - centre) / scale);
            for (int j = 0; j < weights.Length; j++)
                weights[j] /= sum;
            result[i] = new(start, weights);
        }
        return result;
    }

    static double Mitchell(double x) => x < 1
        ? (7 * x * x * x - 12 * x * x + 16d / 3) / 6
        : x < 2 ? (-7d / 3 * x * x * x + 12 * x * x - 20 * x + 32d / 3) / 6 : 0;

    static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    static void Validate(byte[] source, int width, int height, int targetWidth, int targetHeight, int channels)
    {
        if (width < 1 || height < 1 || targetWidth < 1 || targetHeight < 1 ||
            (long)width * height * channels != source.Length || (long)targetWidth * targetHeight * channels > int.MaxValue)
            throw new ArgumentException("Invalid image dimensions or pixel buffer.");
    }
}
