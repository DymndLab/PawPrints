using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.TextureApi;
using Brutal.TextureApi.Abstractions;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using KSA;
using RenderCore;

namespace Blueprint;

// Construct/dispose only on the game's rendering thread, before emitting ImGui draw commands.
sealed class PreviewTexture : IDisposable
{
    SimpleVkTexture? texture;
    VkImageView? view;
    VkSampler? sampler;
    GenericTexture? pixels;
    public ImTextureRef? Image
    {
        get; private set;
    }


    public PreviewTexture(Sheet sheet) : this(sheet.PixelWidth, sheet.PixelHeight, sheet.PreviewRgba) { }


    public PreviewTexture(int width, int height, byte[] rgba)
    {
        var renderer = KSA.Program.GetRenderer();
        try
        {
            pixels = GenericTexture.Defaults.Uncompressed(TextureFormat.R8G8B8A8_UNorm, new int2(width, height));
            rgba.CopyTo(pixels.Data);
            var asset = new TextureAsset(pixels, "Blueprint preview");
            using (var staging = renderer.Allocator.CreateStagingPool(renderer.Graphics, 1))
            {
                texture = new SimpleVkTexture(renderer.Allocator, staging, asset);
                var info = new VkImageViewCreateInfo
                {
                    Image = texture.VkImage,
                    ViewType = VkImageViewType._2D,
                    Format = texture.Format,
                    Components = new() { R = VkComponentSwizzle.R, G = VkComponentSwizzle.G, B = VkComponentSwizzle.B, A = VkComponentSwizzle.A },
                    SubresourceRange = new() { AspectMask = VkImageAspectFlags.ColorBit, BaseMipLevel = 0, LevelCount = texture.MipMapCount, BaseArrayLayer = 0, LayerCount = 1 }
                };
                view = renderer.Device.CreateImageView(in info, null);
            }
            // SimpleVkTexture generates the full mip chain for this uncompressed
            // image. Expose it and interpolate levels so fine ink stays stable at
            // fit-page scale, while zooming can use the full-resolution source.
            var sampling = Presets.Sampler.SamplerLinearClamped;
            sampling.MinFilter = VkFilter.Linear;
            sampling.MagFilter = VkFilter.Linear;
            sampling.MipmapMode = VkSamplerMipmapMode.Linear;
            sampling.MinLod = 0;
            sampling.MaxLod = texture.MipMapCount - 1;
            sampler = renderer.Device.CreateSampler(in sampling, null);
            Image = ImGuiBackend.Vulkan.AddTexture(sampler.Value, view.Value);
        }
        catch { Dispose(); throw; }
    }


    public void Dispose()
    {
        var device = KSA.Program.GetRenderer().Device;
        device.WaitIdle();
        if (Image.HasValue)
        {
            ImGuiBackend.Vulkan.RemoveTexture(Image.Value);
            Image = null;
        }
        if (view.HasValue)
        {
            device.DestroyImageView(view.Value, null);
            view = null;
        }
        texture?.Dispose();
        texture = null;
        if (sampler.HasValue)
        {
            device.DestroySampler(sampler.Value, null);
            sampler = null;
        }
        pixels = null;
    }
}
