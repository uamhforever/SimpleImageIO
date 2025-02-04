using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SimpleImageIO {
    /// <summary>
    /// Reading and writing of multiple named layers to and from files.
    /// Currently, only .exr images are supported.
    /// </summary>
    public static class Layers {
        /// <summary>
        /// Loads a multi-layer .exr file and separates the layers into individual images
        /// </summary>
        /// <param name="filename">Name of an existing .exr image with one or more layers</param>
        /// <returns>A dictionary where layer names are the keys, and the layer images are the values</returns>
        public static Dictionary<string, ImageBase> LoadFromFile(string filename) {
            if (!File.Exists(filename))
                throw new FileNotFoundException("Image file does not exist.", filename);

            // Read the image from the file, it is cached in native memory
            int id = SimpleImageIOCore.CacheImage(out int width, out int height, out _, filename);
            if (id < 0 || width <= 0 || height <= 0)
                throw new IOException($"ERROR: Could not load image file '{filename}'");

            Dictionary<string, ImageBase> layers = new();

            int numLayers = SimpleImageIOCore.GetExrLayerCount(id);
            for (int i = 0; i < numLayers; ++i) {
                int len = SimpleImageIOCore.GetExrLayerNameLen(id, i);
                StringBuilder nameBuilder = new(len);
                SimpleImageIOCore.GetExrLayerName(id, i, nameBuilder);
                string name = nameBuilder.ToString();

                // Create image with the right kind of channels. Assume that 3 channels are always RGB.
                int numChans = SimpleImageIOCore.GetExrLayerChannelCount(id, name);
                if (numChans == 1)
                    layers[name] = new MonochromeImage(width, height);
                else if (numChans == 3)
                    layers[name] = new RgbImage(width, height);
                else
                    layers[name] = new(width, height, numChans);

                SimpleImageIOCore.CopyCachedLayer(id, name, layers[name].DataPointer);
            }

            SimpleImageIOCore.DeleteCachedImage(id);

            return layers;
        }

        /// <summary>
        /// Writes a multi-layer .exr file where each layer is represented by a separate image with one or
        /// more channels
        /// </summary>
        /// <param name="filename">Name of the output .exr file, extension should be .exr</param>
        /// <param name="layers">
        /// Pairs of layer names and layer images to write to the .exr. Must have equal resolutions.
        /// </param>
        public static void WriteToExr(string filename, params (string Name, ImageBase Image)[] layers) {
            ImageBase.EnsureDirectory(filename);

            Array.Sort(layers, (a,b) => a.Name.CompareTo(b.Name));

            // Assemble the raw data in a C-API compatible format
            List<IntPtr> dataPointers = new();
            List<int> strides = new();
            List<int> NumChannels = new();
            List<string> names = new();
            int Width = layers[0].Item2.Width;
            int Height = layers[0].Item2.Height;
            foreach (var (name, img) in layers) {
                dataPointers.Add(img.DataPointer);
                strides.Add(img.NumChannels * img.Width);
                NumChannels.Add(img.NumChannels);
                names.Add(name);
                Debug.Assert(img.Width == Width, "All layers must have the same resolution");
                Debug.Assert(img.Height == Height, "All layers must have the same resolution");
            }

            SimpleImageIOCore.WriteLayeredExr(dataPointers.ToArray(), strides.ToArray(), Width, Height,
                NumChannels.ToArray(), dataPointers.Count, names.ToArray(), filename);
        }
    }
}