﻿using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

/* https://docs.microsoft.com/en-us/windows/desktop/direct3d10/d3d10-graphics-programming-guide-resources-block-compression */
namespace System.Drawing.Dds
{
    public partial class DdsImage
    {
        private delegate byte[] Decompress(byte[] data, int height, int width, bool bgr24);

        private static readonly Dictionary<DxgiFormat, Decompress> decompressMethodsDxgi = new Dictionary<DxgiFormat, Decompress>
        {
            { DxgiFormat.BC1_Typeless, DecompressBC1 },
            { DxgiFormat.BC1_UNorm, DecompressBC1 },
            { DxgiFormat.BC2_Typeless, DecompressBC2 },
            { DxgiFormat.BC2_UNorm, DecompressBC2 },
            { DxgiFormat.BC3_Typeless, DecompressBC3 },
            { DxgiFormat.BC3_UNorm, DecompressBC3 },
            { DxgiFormat.BC4_Typeless, DecompressBC4 },
            { DxgiFormat.BC4_UNorm, DecompressBC4 },
            { DxgiFormat.BC4_SNorm, DecompressBC4 },
            { DxgiFormat.BC5_Typeless, DecompressBC5 },
            { DxgiFormat.BC5_UNorm, DecompressBC5 },
            { DxgiFormat.BC5_SNorm, DecompressBC5 },
            { DxgiFormat.B5G6R5_UNorm, DecompressB5G6R5 },
            { DxgiFormat.B5G5R5A1_UNorm, DecompressB5G5R5A1 },
            { DxgiFormat.P8, DecompressY8 },
            { DxgiFormat.B4G4R4A4_UNorm, DecompressB4G4R4A4 }
        };

        private static readonly Dictionary<XboxFormat, Decompress> decompressMethodsXbox = new Dictionary<XboxFormat, Decompress>
        {
            { XboxFormat.A8, DecompressA8 },
            { XboxFormat.AY8, DecompressAY8 },
            { XboxFormat.CTX1, DecompressCTX1 },
            { XboxFormat.DXN, DecompressDXN },
            { XboxFormat.DXN_mono_alpha, DecompressDXN_mono_alpha },
            { XboxFormat.DXT3a_scalar, DecompressDXT3a_scalar },
            { XboxFormat.DXT3a_mono, DecompressDXT3a_mono },
            { XboxFormat.DXT3a_alpha, DecompressDXT3a_alpha },
            { XboxFormat.DXT5a_scalar, DecompressDXT5a_scalar },
            { XboxFormat.DXT5a_mono, DecompressDXT5a_mono },
            { XboxFormat.DXT5a_alpha, DecompressDXT5a_alpha },
            { XboxFormat.Y8, DecompressY8 },
            { XboxFormat.Y8A8, DecompressY8A8 },
        };

        private static readonly Dictionary<FourCC, Decompress> decompressMethodsFourCC = new Dictionary<FourCC, Decompress>
        {
            { FourCC.DXT1, DecompressBC1 },
            { FourCC.DXT2, DecompressBC2 },
            { FourCC.DXT3, DecompressBC2 },
            { FourCC.DXT4, DecompressBC3 },
            { FourCC.DXT5, DecompressBC3 },
            { FourCC.BC4U, DecompressBC4 },
            { FourCC.BC4S, DecompressBC4 },
            { FourCC.ATI1, DecompressBC4 },
            { FourCC.BC5U, DecompressBC5 },
            { FourCC.BC5S, DecompressBC5 },
            { FourCC.ATI2, DecompressBC5 },
        };

        #region WriteToDisk
        /// <summary>
        /// Decompresses any compressed pixel data and saves the image to a file on disk using a standard image format.
        /// </summary>
        /// <param name="fileName">The full path of the file to write.</param>
        /// <param name="format">The image format to write with.</param>
        /// <exception cref="ArgumentNullException" />
        /// <exception cref="NotSupportedException" />
        public void WriteToDisk(string fileName, ImageFormat format) => WriteToDisk(fileName, format, DecompressOptions.Default);

        /// <summary>
        /// Decompresses any compressed pixel data and saves the image to a file on disk using a standard image format,
        /// optionally unwrapping cubemap images.
        /// </summary>
        /// <param name="fileName">The full path of the file to write.</param>
        /// <param name="format">The image format to write with.</param>
        /// <param name="unwrapCubemap">True to unwrap a cubemap. False to output each tile horizontally.</param>
        /// <exception cref="ArgumentNullException" />
        /// <exception cref="NotSupportedException" />
        public void WriteToDisk(string fileName, ImageFormat format, DecompressOptions options)
        {
            if (fileName == null)
                throw new ArgumentNullException(nameof(fileName));

            if (format == null)
                throw new ArgumentNullException(nameof(format));

            var dir = Directory.GetParent(fileName).FullName;

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write))
                WriteToStream(fs, format, options);
        }
        #endregion

        #region WriteToStream
        /// <summary>
        /// Decompresses any compressed pixel data and writes the image to a stream using a standard image format
        /// using the default decompression options and a non-cubemap layout.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="format">The image format to write with.</param>
        /// <exception cref="ArgumentNullException" />
        /// <exception cref="NotSupportedException" />
        public void WriteToStream(Stream stream, ImageFormat format) => WriteToStream(stream, format, DecompressOptions.Default, CubemapLayout.NonCubemap);

        /// <summary>
        /// Decompresses any compressed pixel data and writes the image to a stream using a standard image format
        /// using the specified decompression options and a non-cubemap layout.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="format">The image format to write with.</param>
        /// <param name="options">Options to use when decompressing the image.</param>
        /// <exception cref="ArgumentNullException" />
        /// <exception cref="NotSupportedException" />
        public void WriteToStream(Stream stream, ImageFormat format, DecompressOptions options) => WriteToStream(stream, format, options, CubemapLayout.NonCubemap);

        /// <summary>
        /// Decompresses any compressed pixel data and writes the image to a stream using a standard image format
        /// using the default decompression options the specified cubemap layout.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="format">The image format to write with.</param>
        /// <param name="layout">The layout of the cubemap. Has no effect if the DDS cubemap flags are not set.</param>
        /// <exception cref="ArgumentNullException" />
        /// <exception cref="NotSupportedException" />
        public void WriteToStream(Stream stream, ImageFormat format, CubemapLayout layout) => WriteToStream(stream, format, DecompressOptions.Default, layout);

        /// <summary>
        /// Decompresses any compressed pixel data and writes the image to a stream using a standard image format.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="format">The image format to write with.</param>
        /// <param name="options">Options to use when decompressing the image.</param>
        /// <param name="layout">The layout of the cubemap. Has no effect if the DDS cubemap flags are not set.</param>
        /// <exception cref="ArgumentNullException" />
        /// <exception cref="NotSupportedException" />
        public void WriteToStream(Stream stream, ImageFormat format, DecompressOptions options, CubemapLayout layout)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (format == null)
                throw new ArgumentNullException(nameof(format));

            BitmapEncoder encoder;
            if (format.Equals(ImageFormat.Bmp))
                encoder = new BmpBitmapEncoder();
            else if (format.Equals(ImageFormat.Gif))
                encoder = new GifBitmapEncoder();
            else if (format.Equals(ImageFormat.Jpeg))
                encoder = new JpegBitmapEncoder();
            else if (format.Equals(ImageFormat.Png))
                encoder = new PngBitmapEncoder();
            else if (format.Equals(ImageFormat.Tiff))
                encoder = new TiffBitmapEncoder();
            else throw new NotSupportedException("The ImageFormat is not supported.");

            var source = ToBitmapSource(options, layout);
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(stream);
        }
        #endregion

        #region ToBitmapSource
        /// <summary>
        /// Decompresses any compressed pixel data and returns the image data as a <see cref="BitmapSource"/>.
        /// </summary>
        /// <exception cref="NotSupportedException" />
        public BitmapSource ToBitmapSource() => ToBitmapSource(DecompressOptions.Default, CubemapLayout.NonCubemap);

        /// <summary>
        /// Decompresses any compressed pixel data and returns the image data as a <see cref="BitmapSource"/>.
        /// </summary>
        /// <exception cref="NotSupportedException" />
        public BitmapSource ToBitmapSource(DecompressOptions options) => ToBitmapSource(options, CubemapLayout.NonCubemap);

        /// <summary>
        /// Decompresses any compressed pixel data and returns the image data as a <see cref="BitmapSource"/>.
        /// </summary>
        /// <exception cref="NotSupportedException" />
        public BitmapSource ToBitmapSource(CubemapLayout layout) => ToBitmapSource(DecompressOptions.Default, layout);

        /// <summary>
        /// Decompresses any compressed pixel data and returns the image data as a <see cref="BitmapSource"/>
        /// </summary>
        /// <param name="options">Options to use during decompression.</param>
        /// <param name="layout">The layout of the cubemap. Has no effect if the DDS cubemap flags are not set.</param>
        /// <exception cref="NotSupportedException" />
        public BitmapSource ToBitmapSource(DecompressOptions options, CubemapLayout layout)
        {
            const double dpi = 96;
            var virtualHeight = Height;

            var isCubeMap = TextureFlags.HasFlag(TextureFlags.DdsSurfaceFlagsCubemap) && CubemapFlags.HasFlag(CubemapFlags.DdsCubemapAllFaces);
            if (isCubeMap) virtualHeight *= 6;

            var bgr24 = options.HasFlag(DecompressOptions.Bgr24);

            byte[] pixels;
            if (header.PixelFormat.FourCC == (uint)FourCC.DX10)
            {
                if (decompressMethodsDxgi.ContainsKey(dx10Header.DxgiFormat))
                    pixels = decompressMethodsDxgi[dx10Header.DxgiFormat](data, virtualHeight, Width, bgr24);
                else
                {
                    switch (dx10Header.DxgiFormat)
                    {
                        case DxgiFormat.B8G8R8X8_UNorm:
                        case DxgiFormat.B8G8R8A8_UNorm:
                            pixels = bgr24 ? ToArray(SkipNth(data, 4), true, virtualHeight, Width) : data;
                            break;

                        default: throw new NotSupportedException("The DxgiFormat is not supported.");
                    }
                }
            }
            else if (header.PixelFormat.FourCC == (uint)FourCC.XBOX)
            {
                if (decompressMethodsXbox.ContainsKey(xboxHeader.XboxFormat))
                    pixels = decompressMethodsXbox[xboxHeader.XboxFormat](data, virtualHeight, Width, bgr24);
                else throw new NotSupportedException("The XboxFormat is not supported.");
            }
            else
            {
                var fourcc = (FourCC)header.PixelFormat.FourCC;
                if (decompressMethodsFourCC.ContainsKey(fourcc))
                    pixels = decompressMethodsFourCC[fourcc](data, virtualHeight, Width, bgr24);
                else throw new NotSupportedException("The FourCC is not supported.");
            }

            var format = bgr24 ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
            var bpp = bgr24 ? 3 : 4;

            //at least one 'remove channel' flag is set
            if ((options & DecompressOptions.RemoveAllChannels) != 0)
                MaskChannels(pixels, options);

            var source = BitmapSource.Create(Width, virtualHeight, dpi, dpi, format, null, pixels, Width * bpp);

            if (isCubeMap && layout.IsValid)
                source = UnwrapCubemapSource(source, dpi, format, layout);

            return source;
        }
        #endregion

        private void MaskChannels(byte[] source, DecompressOptions channels)
        {
            var bpp = channels.HasFlag(DecompressOptions.Bgr24) ? 3 : 4;
            int mask = 0;

            if (!channels.HasFlag(DecompressOptions.RemoveBlueChannel)) mask |= 1;
            if (!channels.HasFlag(DecompressOptions.RemoveGreenChannel)) mask |= 2;
            if (!channels.HasFlag(DecompressOptions.RemoveRedChannel)) mask |= 4;
            if (!channels.HasFlag(DecompressOptions.RemoveAlphaChannel)) mask |= 8;

            int channelIndex;
            if (mask == 1) channelIndex = 0;
            else if (mask == 2) channelIndex = 1;
            else if (mask == 4) channelIndex = 2;
            else if (mask == 8) channelIndex = 3;
            else channelIndex = -1;

            for (int i = 0; i < source.Length; i += bpp)
            {
                for (int j = 0; j < bpp; j++)
                {
                    if (channelIndex >= 0)
                    {
                        if (j == 3) source[i + j] = byte.MaxValue; //full opacity
                        else source[i + j] = channelIndex < bpp ? source[i + channelIndex] : byte.MinValue;
                    }
                    else
                    {
                        var bit = (int)Math.Pow(2, j);
                        if ((mask & bit) == 0)
                            source[i + j] = j < 3 ? byte.MinValue : byte.MaxValue;
                    }
                }
            }
        }

        private BitmapSource UnwrapCubemapSource(BitmapSource source, double dpi, Windows.Media.PixelFormat format, CubemapLayout layout)
        {
            var bpp = format.BitsPerPixel / 8;
            var stride = bpp * Width;
            var dest = new WriteableBitmap(Width * 4, Height * 3, dpi, dpi, format, null);

            var faceArray = new[] { layout.Face1, layout.Face2, layout.Face3, layout.Face4, layout.Face5, layout.Face6 };
            var rotateArray = new[] { layout.Orientation1, layout.Orientation2, layout.Orientation3, layout.Orientation4, layout.Orientation5, layout.Orientation6 };

            var xTiles = new[] { 1, 0, 1, 2, 3, 1 };
            var yTiles = new[] { 0, 1, 1, 1, 1, 2 };

            for (int i = 0; i < 6; i++)
            {
                var tileIndex = (int)faceArray[i] - 1;

                var sourceRect = new Int32Rect(0, Height * i, Width, Height);
                var destRect = new Int32Rect(xTiles[tileIndex] * Width, yTiles[tileIndex] * Height, Width, Height);

                var buffer = new byte[Width * Height * bpp];
                source.CopyPixels(sourceRect, buffer, stride, 0);
                buffer = Rotate(buffer, Width, Height, bpp, rotateArray[i]);
                dest.WritePixels(destRect, buffer, stride, 0);
            }

            return dest;
        }

        #region Standard Decompression Methods
        internal static byte[] DecompressB5G6R5(byte[] source, int height, int width, bool bgr24)
        {
            return ToArray(Enumerable.Range(0, height * width).SelectMany(i => BgraColour.From565(BitConverter.ToUInt16(source, i * 2)).AsEnumerable(bgr24)), bgr24, height, width);
        }

        internal static byte[] DecompressB5G5R5A1(byte[] data, int height, int width, bool bgr24)
        {
            return ToArray(Enumerable.Range(0, height * width).SelectMany(i => BgraColour.From5551(BitConverter.ToUInt16(data, i * 2)).AsEnumerable(bgr24)), bgr24, height, width);
        }

        internal static byte[] DecompressB4G4R4A4(byte[] data, int height, int width, bool bgr24)
        {
            return ToArray(Enumerable.Range(0, height * width).SelectMany(i => BgraColour.From4444(BitConverter.ToUInt16(data, i * 2)).AsEnumerable(bgr24)), bgr24, height, width);
        }

        internal static byte[] DecompressBC1(byte[] data, int height, int width, bool bgr24)
        {
            var bpp = bgr24 ? 3 : 4;
            var output = new byte[width * height * bpp];
            var palette = new BgraColour[4];

            const int bytesPerBlock = 8;
            var xBlocks = width / 4;
            var yBlocks = height / 4;

            for (int yBlock = 0; yBlock < yBlocks; yBlock++)
            {
                for (int xBlock = 0; xBlock < xBlocks; xBlock++)
                {
                    var srcIndex = (yBlock * xBlocks + xBlock) * bytesPerBlock;
                    var c0 = BitConverter.ToUInt16(data, srcIndex);
                    var c1 = BitConverter.ToUInt16(data, srcIndex + 2);

                    palette[0] = BgraColour.From565(c0);
                    palette[1] = BgraColour.From565(c1);

                    if (c0 <= c1)
                    {
                        palette[2] = Lerp(palette[0], palette[1], 1 / 2f);
                        palette[3] = new BgraColour(); //zero on all channels
                    }
                    else
                    {
                        palette[2] = Lerp(palette[0], palette[1], 1 / 3f);
                        palette[3] = Lerp(palette[0], palette[1], 2 / 3f);
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        var indexBits = data[srcIndex + 4 + i];
                        for (int j = 0; j < 4; j++)
                        {
                            var destX = xBlock * 4 + j;
                            var destY = yBlock * 4 + i;

                            var destIndex = (destY * width + destX) * bpp;
                            var pIndex = (byte)((indexBits >> j * 2) & 0x3);
                            palette[pIndex].Copy(output, destIndex, bgr24);
                        }
                    }
                }
            }

            return output;
        }

        internal static byte[] DecompressBC2(byte[] data, int height, int width, bool bgr24)
        {
            var bpp = bgr24 ? 3 : 4;
            var output = new byte[width * height * bpp];
            var palette = new BgraColour[4];

            const int bytesPerBlock = 16;
            var xBlocks = width / 4;
            var yBlocks = height / 4;

            for (int yBlock = 0; yBlock < yBlocks; yBlock++)
            {
                for (int xBlock = 0; xBlock < xBlocks; xBlock++)
                {
                    var srcIndex = (yBlock * xBlocks + xBlock) * bytesPerBlock;
                    palette[0] = BgraColour.From565(BitConverter.ToUInt16(data, srcIndex + 8));
                    palette[1] = BgraColour.From565(BitConverter.ToUInt16(data, srcIndex + 10));

                    palette[2] = Lerp(palette[0], palette[1], 1 / 3f);
                    palette[3] = Lerp(palette[0], palette[1], 2 / 3f);

                    for (int i = 0; i < 4; i++)
                    {
                        var alphaBits = BitConverter.ToUInt16(data, srcIndex + i * 2);
                        var indexBits = data[srcIndex + 12 + i];
                        for (int j = 0; j < 4; j++)
                        {
                            var destX = xBlock * 4 + j;
                            var destY = yBlock * 4 + i;

                            var destIndex = (destY * width + destX) * bpp;
                            var pIndex = (byte)((indexBits >> j * 2) & 0x3);

                            var result = palette[pIndex];
                            result.a = (byte)(((alphaBits >> j * 4) & 0xF) * (0xFF / 0xF));
                            result.Copy(output, destIndex, bgr24);
                        }
                    }
                }
            }

            return output;
        }

        internal static byte[] DecompressBC3(byte[] data, int height, int width, bool bgr24)
        {
            var bpp = bgr24 ? 3 : 4;
            var output = new byte[width * height * bpp];
            var rgbPalette = new BgraColour[4];
            var alphaPalette = new byte[8];

            const int bytesPerBlock = 16;
            var xBlocks = width / 4;
            var yBlocks = height / 4;

            for (int yBlock = 0; yBlock < yBlocks; yBlock++)
            {
                for (int xBlock = 0; xBlock < xBlocks; xBlock++)
                {
                    var srcIndex = (yBlock * xBlocks + xBlock) * bytesPerBlock;
                    rgbPalette[0] = BgraColour.From565(BitConverter.ToUInt16(data, srcIndex + 8));
                    rgbPalette[1] = BgraColour.From565(BitConverter.ToUInt16(data, srcIndex + 10));

                    rgbPalette[2] = Lerp(rgbPalette[0], rgbPalette[1], 1 / 3f);
                    rgbPalette[3] = Lerp(rgbPalette[0], rgbPalette[1], 2 / 3f);

                    alphaPalette[0] = data[srcIndex];
                    alphaPalette[1] = data[srcIndex + 1];

                    var gradients = alphaPalette[0] > alphaPalette[1] ? 7f : 5f;
                    for (int i = 1; i < gradients; i++)
                        alphaPalette[i + 1] = Lerp(alphaPalette[0], alphaPalette[1], i / gradients);

                    if (alphaPalette[0] <= alphaPalette[1])
                    {
                        alphaPalette[6] = byte.MinValue;
                        alphaPalette[7] = byte.MaxValue;
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        var rgbIndexBits = data[srcIndex + 12 + i];
                        for (int j = 0; j < 4; j++)
                        {
                            var pixelIndex = i * 4 + j;
                            var alphaStart = srcIndex + (pixelIndex < 8 ? 2 : 5);
                            var alphaIndexBits = (data[alphaStart + 2] << 16) | (data[alphaStart + 1] << 8) | data[alphaStart];

                            var destX = xBlock * 4 + j;
                            var destY = yBlock * 4 + i;

                            var destIndex = (destY * width + destX) * bpp;
                            var pIndex = (byte)((rgbIndexBits >> j * 2) & 0x3);

                            var result = rgbPalette[pIndex];
                            result.a = alphaPalette[(alphaIndexBits >> (pixelIndex % 8) * 3) & 0x7];
                            result.Copy(output, destIndex, bgr24);
                        }
                    }
                }
            }

            return output;
        }

        internal static byte[] DecompressBC4(byte[] data, int height, int width, bool bgr24)
        {
            var bpp = bgr24 ? 3 : 4;
            var output = new byte[width * height * bpp];
            var palette = new byte[8];

            const int bytesPerBlock = 8;
            var xBlocks = width / 4;
            var yBlocks = height / 4;

            for (int yBlock = 0; yBlock < yBlocks; yBlock++)
            {
                for (int xBlock = 0; xBlock < xBlocks; xBlock++)
                {
                    var srcIndex = (yBlock * xBlocks + xBlock) * bytesPerBlock;
                    palette[0] = data[srcIndex];
                    palette[1] = data[srcIndex + 1];

                    var gradients = palette[0] > palette[1] ? 7f : 5f;
                    for (int i = 1; i < gradients; i++)
                        palette[i + 1] = Lerp(palette[0], palette[1], i / gradients);

                    if (palette[0] <= palette[1])
                    {
                        palette[6] = byte.MinValue;
                        palette[7] = byte.MaxValue;
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        for (int j = 0; j < 4; j++)
                        {
                            var pixelIndex = i * 4 + j;
                            var pStart = srcIndex + (pixelIndex < 8 ? 2 : 5);
                            var pIndexBits = (data[pStart + 2] << 16) | (data[pStart + 1] << 8) | data[pStart];

                            var destX = xBlock * 4 + j;
                            var destY = yBlock * 4 + i;

                            var destIndex = (destY * width + destX) * bpp;
                            var pIndex = (byte)((pIndexBits >> (pixelIndex % 8) * 3) & 0x7);

                            output[destIndex] = output[destIndex + 1] = output[destIndex + 2] = palette[pIndex];
                            if (!bgr24) output[destIndex + 3] = byte.MaxValue;
                        }
                    }
                }
            }

            return output;
        }

        internal static byte[] DecompressBC5(byte[] data, int height, int width, bool bgr24)
        {
            var bpp = bgr24 ? 3 : 4;
            var output = new byte[width * height * bpp];
            var rPalette = new byte[8];
            var gPalette = new byte[8];

            const int bytesPerBlock = 16;
            var xBlocks = width / 4;
            var yBlocks = height / 4;

            for (int yBlock = 0; yBlock < yBlocks; yBlock++)
            {
                for (int xBlock = 0; xBlock < xBlocks; xBlock++)
                {
                    var srcIndex = (yBlock * xBlocks + xBlock) * bytesPerBlock;

                    rPalette[0] = data[srcIndex];
                    rPalette[1] = data[srcIndex + 1];

                    var gradients = rPalette[0] > rPalette[1] ? 7f : 5f;
                    for (int i = 1; i < gradients; i++)
                        rPalette[i + 1] = Lerp(rPalette[0], rPalette[1], i / gradients);

                    if (rPalette[0] <= rPalette[1])
                    {
                        rPalette[6] = byte.MinValue;
                        rPalette[7] = byte.MaxValue;
                    }

                    gPalette[0] = data[srcIndex + 8];
                    gPalette[1] = data[srcIndex + 9];

                    gradients = gPalette[0] > gPalette[1] ? 7f : 5f;
                    for (int i = 1; i < gradients; i++)
                        gPalette[i + 1] = Lerp(gPalette[0], gPalette[1], i / gradients);

                    if (gPalette[0] <= gPalette[1])
                    {
                        gPalette[6] = byte.MinValue;
                        gPalette[7] = byte.MaxValue;
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        for (int j = 0; j < 4; j++)
                        {
                            var pixelIndex = i * 4 + j;

                            var rStart = srcIndex + (pixelIndex < 8 ? 2 : 5);
                            var rIndexBits = (data[rStart + 2] << 16) | (data[rStart + 1] << 8) | data[rStart];

                            var gStart = srcIndex + (pixelIndex < 8 ? 10 : 13);
                            var gIndexBits = (data[gStart + 2] << 16) | (data[gStart + 1] << 8) | data[gStart];

                            var destX = xBlock * 4 + j;
                            var destY = yBlock * 4 + i;

                            var destIndex = (destY * width + destX) * bpp;
                            var shift = (pixelIndex % 8) * 3;

                            var rIndex = (byte)((rIndexBits >> shift) & 0x7);
                            var gIndex = (byte)((gIndexBits >> shift) & 0x7);

                            //output[destIndex] = byte.MinValue;
                            output[destIndex + 1] = gPalette[gIndex];
                            output[destIndex + 2] = rPalette[rIndex];
                            if (!bgr24) output[destIndex + 3] = byte.MaxValue;
                        }
                    }
                }
            }

            return output;
        }
        #endregion

        #region Xbox Decompression Methods
        internal static byte[] DecompressA8(byte[] data, int height, int width, bool bgr24)
        {
            return ToArray(data.SelectMany(b => Enumerable.Range(0, bgr24 ? 3 : 4).Select(i => i < 3 ? byte.MinValue : b)), bgr24, height, width);
        }

        internal static byte[] DecompressAY8(byte[] data, int height, int width, bool bgr24)
        {
            return ToArray(data.SelectMany(b => Enumerable.Range(0, bgr24 ? 3 : 4).Select(i => b)), bgr24, height, width);
        }

        internal static byte[] DecompressY8(byte[] data, int height, int width, bool bgr24)
        {
            return ToArray(data.SelectMany(b => Enumerable.Range(0, bgr24 ? 3 : 4).Select(i => i < 3 ? b : byte.MaxValue)), bgr24, height, width);
        }

        internal static byte[] DecompressY8A8(byte[] data, int height, int width, bool bgr24)
        {
            return ToArray(Enumerable.Range(0, height * width).SelectMany(i => Enumerable.Range(0, bgr24 ? 3 : 4).Select(j => j < 3 ? data[i * 2 + 1] : data[i * 2])), bgr24, height, width);
        }

        internal static byte[] DecompressBC1DualChannel(byte[] data, int height, int width, bool bgr24)
        {
            var bpp = bgr24 ? 3 : 4;
            var output = new byte[width * height * bpp];
            var palette = new BgraColour[4];

            const int bytesPerBlock = 8;
            var xBlocks = width / 4;
            var yBlocks = height / 4;

            for (int yBlock = 0; yBlock < yBlocks; yBlock++)
            {
                for (int xBlock = 0; xBlock < xBlocks; xBlock++)
                {
                    var srcIndex = (yBlock * xBlocks + xBlock) * bytesPerBlock;
                    palette[0] = new BgraColour { g = data[srcIndex + 0], r = data[srcIndex + 1], a = byte.MaxValue };
                    palette[1] = new BgraColour { g = data[srcIndex + 2], r = data[srcIndex + 3], a = byte.MaxValue };

                    palette[2] = Lerp(palette[0], palette[1], 1 / 3f);
                    palette[3] = Lerp(palette[0], palette[1], 2 / 3f);

                    for (int i = 0; i < 4; i++)
                    {
                        var indexBits = data[srcIndex + 4 + i];
                        for (int j = 0; j < 4; j++)
                        {
                            var destX = xBlock * 4 + j;
                            var destY = yBlock * 4 + i;

                            var destIndex = (destY * width + destX) * bpp;
                            var pIndex = (byte)((indexBits >> j * 2) & 0x3);
                            var colour = palette[pIndex];
                            colour.b = CalculateZVector(colour.r, colour.g);
                            colour.Copy(output, destIndex, bgr24);
                        }
                    }
                }
            }

            return output;
        }

        internal static byte[] DecompressBC2AlphaOnly(byte[] data, int height, int width, bool bgr, bool a, bool bgr24)
        {
            var bpp = bgr24 ? 3 : 4;
            var output = new byte[width * height * bpp];

            const int bytesPerBlock = 8;
            var xBlocks = width / 4;
            var yBlocks = height / 4;

            for (int yBlock = 0; yBlock < yBlocks; yBlock++)
            {
                for (int xBlock = 0; xBlock < xBlocks; xBlock++)
                {
                    var srcIndex = (yBlock * xBlocks + xBlock) * bytesPerBlock;
                    for (int i = 0; i < 4; i++)
                    {
                        var alphaBits = BitConverter.ToUInt16(data, srcIndex + i * 2);
                        for (int j = 0; j < 4; j++)
                        {
                            var destX = xBlock * 4 + j;
                            var destY = yBlock * 4 + i;
                            var destIndex = (destY * width + destX) * bpp;

                            var value = (byte)(((alphaBits >> j * 4) & 0xF) * (0xFF / 0xF));
                            if (bgr) output[destIndex] = output[destIndex + 1] = output[destIndex + 2] = value;
                            if (!bgr24) output[destIndex + 3] = a ? value : byte.MaxValue;
                        }
                    }
                }
            }

            return output;
        }

        internal static byte[] DecompressBC3AlphaOnly(byte[] data, int height, int width, bool bgr, bool a, bool bgr24)
        {
            //same bit layout as BC4
            data = DecompressBC4(data, height, width, bgr24);

            for (int i = 0; i < data.Length; i += 4)
            {
                data[i + 1] = data[i + 2] = bgr ? data[i] : byte.MinValue; //gr = b
                if (!bgr24) data[i + 3] = a ? data[i] : byte.MaxValue; //a = b
            }

            return data;
        }

        internal static byte[] DecompressCTX1(byte[] data, int height, int width, bool bgr24)
        {
            return DecompressBC1DualChannel(data, height, width, bgr24);
        }

        internal static byte[] DecompressDXN(byte[] data, int height, int width, bool bgr24)
        {
            var bpp = bgr24 ? 3 : 4;
            data = DecompressBC5(data, height, width, bgr24);
            for (int i = 0; i < data.Length; i += bpp)
                data[i] = CalculateZVector(data[i + 2], data[i + 1]);

            return data;
        }

        internal static byte[] DecompressDXN_mono_alpha(byte[] data, int height, int width, bool bgr24)
        {
            var bpp = bgr24 ? 3 : 4;
            data = DecompressBC5(data, height, width, bgr24);
            for (int i = 0; i < data.Length; i += bpp)
            {
                if (!bgr24) data[i + 3] = data[i + 1]; //a = g
                data[i] = data[i + 1] = data[i + 2]; //bg = r
            }

            return data;
        }

        internal static byte[] DecompressDXT3a_scalar(byte[] data, int height, int width, bool bgr24)
        {
            return DecompressBC2AlphaOnly(data, height, width, true, true, bgr24);
        }

        internal static byte[] DecompressDXT3a_mono(byte[] data, int height, int width, bool bgr24)
        {
            return DecompressBC2AlphaOnly(data, height, width, true, false, bgr24);
        }

        internal static byte[] DecompressDXT3a_alpha(byte[] data, int height, int width, bool bgr24)
        {
            return DecompressBC2AlphaOnly(data, height, width, false, true, bgr24);
        }

        internal static byte[] DecompressDXT5a_scalar(byte[] data, int height, int width, bool bgr24)
        {
            return DecompressBC3AlphaOnly(data, height, width, true, true, bgr24);
        }

        internal static byte[] DecompressDXT5a_mono(byte[] data, int height, int width, bool bgr24)
        {
            return DecompressBC3AlphaOnly(data, height, width, true, false, bgr24);
        }

        internal static byte[] DecompressDXT5a_alpha(byte[] data, int height, int width, bool bgr24)
        {
            return DecompressBC3AlphaOnly(data, height, width, false, true, bgr24);
        }
        #endregion

        private static byte Lerp(byte p1, byte p2, float fraction)
        {
            return (byte)((p1 * (1 - fraction)) + (p2 * fraction));
        }

        private static float Lerp(float p1, float p2, float fraction)
        {
            return (p1 * (1 - fraction)) + (p2 * fraction);
        }

        private static byte CalculateZVector(byte r, byte g)
        {
            var x = Lerp(-1f, 1f, r / 255f);
            var y = Lerp(-1f, 1f, g / 255f);
            var z = (float)Math.Sqrt(1 - x * x - y * y);

            return (byte)((z + 1) / 2 * 255f);
        }

        private static byte[] Rotate(byte[] buffer, int width, int height, int bpp, RotateFlipType rotation)
        {
            var rot = (int)rotation;

            if (rot == 0)
                return buffer;

            var turns = 4 - rot % 4; //starting at 4 because we need to undo the rotations, not apply them
            var output = new byte[buffer.Length];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var sourceIndex = y * width * bpp + x * bpp;

                    int destW = rot % 2 == 0 ? width : height;
                    int destH = rot % 2 == 0 ? height : width;

                    int destX, destY;
                    if (turns == 0)
                    {
                        destX = x;
                        destY = y;
                    }
                    else if (turns == 1)
                    {
                        destY = x;
                        destX = (height - 1) - y;
                    }
                    else if (turns == 2)
                    {
                        destY = (height - 1) - y;
                        destX = (width - 1) - x;
                    }
                    else //if (turns == 3)
                    {
                        destY = (width - 1) - x;
                        destX = y;
                    }

                    if (rot > 3) //flip X
                        destX = (destW - 1) - destX;

                    var destIndex = destY * destW * bpp + destX * bpp;
                    for (int i = 0; i < bpp; i++)
                        output[destIndex + i] = buffer[sourceIndex + i];
                }
            }

            return output;
        }

        private static BgraColour Lerp(BgraColour c0, BgraColour c1, float fraction)
        {
            return new BgraColour
            {
                b = Lerp(c0.b, c1.b, fraction),
                g = Lerp(c0.g, c1.g, fraction),
                r = Lerp(c0.r, c1.r, fraction),
                a = Lerp(c0.a, c1.a, fraction)
            };
        }

        private static IEnumerable<T> SkipNth<T>(IEnumerable<T> enumerable, int n)
        {
            int i = 0;
            foreach (var item in enumerable)
            {
                if (++i != n)
                    yield return item;
                else i = 0;
            }
        }

        private static byte[] ToArray(IEnumerable<byte> source, bool bgr24, int height, int width)
        {
            var len = height * width * (bgr24 ? 3 : 4);
            var arraySource = source as byte[];
            if (arraySource?.Length >= len)
            {
                if (arraySource.Length == len)
                    return arraySource;

                var subArray = new byte[len];
                Array.Copy(arraySource, subArray, len);
                return subArray;
            }

            var output = new byte[len];
            int i = 0;
            foreach (var b in source)
            {
                output[i++] = b;
                if (i >= output.Length)
                    break;
            }
            return output;
        }
    }

    [Flags]
    public enum DecompressOptions
    {
        /// <summary>
        /// The default option. If no other flags are specified, 32bpp BGRA will be used.
        /// </summary>
        Default = 0,

        /// <summary>
        /// Outputs pixel data in 24bpp BGR format. Does not output an alpha channel regardless of any other flags specified.
        /// </summary>
        Bgr24 = 1,

        /// <summary>
        /// Replaces all blue channel data with zeros.
        /// </summary>
        RemoveBlueChannel = 2,

        /// <summary>
        /// Replaces all green channel data with zeros.
        /// </summary>
        RemoveGreenChannel = 4,

        /// <summary>
        /// Replaces all red channel data with zeros.
        /// </summary>
        RemoveRedChannel = 8,

        /// <summary>
        /// Replaces all alpha channel data with full opacity.
        /// </summary>
        RemoveAlphaChannel = 16,

        /// <summary>
        /// Replicates the blue channel data over the green and red channels. The alpha channel will be fully opaque.
        /// </summary>
        BlueChannelOnly = RemoveGreenChannel | RemoveRedChannel | RemoveAlphaChannel,

        /// <summary>
        /// Replicates the green channel data over the blue and red channels. The alpha channel will be fully opaque.
        /// </summary>
        GreenChannelOnly = RemoveBlueChannel | RemoveRedChannel | RemoveAlphaChannel,

        /// <summary>
        /// Replicates the red channel data over the blue and green and channels. The alpha channel will be fully opaque.
        /// </summary>
        RedChannelOnly = RemoveBlueChannel | RemoveGreenChannel | RemoveAlphaChannel,

        /// <summary>
        /// Replicates the alpha channel data over the blue, green and red channels. The alpha channel will be fully opaque.
        /// </summary>
        AlphaChannelOnly = RemoveBlueChannel | RemoveGreenChannel | RemoveRedChannel,

        /// <summary>
        /// Produces a solid black image with opaque alpha.
        /// </summary>
        RemoveAllChannels = RemoveBlueChannel | RemoveGreenChannel | RemoveRedChannel | RemoveAlphaChannel
    }

    public enum CubemapFace
    {
        None,
        Top,
        Left,
        Front,
        Right,
        Back,
        Bottom
    }

    public class CubemapLayout
    {
        private static readonly CubemapLayout invalid = new CubemapLayout();
        public static CubemapLayout NonCubemap => invalid;

        public CubemapFace Face1 { get; set; }
        public CubemapFace Face2 { get; set; }
        public CubemapFace Face3 { get; set; }
        public CubemapFace Face4 { get; set; }
        public CubemapFace Face5 { get; set; }
        public CubemapFace Face6 { get; set; }

        public RotateFlipType Orientation1 { get; set; }
        public RotateFlipType Orientation2 { get; set; }
        public RotateFlipType Orientation3 { get; set; }
        public RotateFlipType Orientation4 { get; set; }
        public RotateFlipType Orientation5 { get; set; }
        public RotateFlipType Orientation6 { get; set; }

        public bool IsValid => (Face1 | Face2 | Face3 | Face4 | Face5 | Face6) > 0;
    }

    internal struct BgraColour
    {
        public byte b, g, r, a;

        public IEnumerable<byte> AsEnumerable(bool bgr24)
        {
            yield return b;
            yield return g;
            yield return r;
            if (!bgr24) yield return a;
        }

        public void Copy(byte[] destination, int destinationIndex, bool bgr24)
        {
            destination[destinationIndex] = b;
            destination[destinationIndex + 1] = g;
            destination[destinationIndex + 2] = r;
            if (!bgr24) destination[destinationIndex + 3] = a;
        }

        public static BgraColour From565(ushort value)
        {
            const byte BMask = 0x1F;
            const byte GMask = 0x3F;
            const byte RMask = 0x1F;

            return new BgraColour
            {
                b = (byte)((0xFF / BMask) * (value & BMask)),
                g = (byte)((0xFF / GMask) * ((value >> 5) & GMask)),
                r = (byte)((0xFF / RMask) * ((value >> 11) & RMask)),
                a = byte.MaxValue
            };
        }

        public static BgraColour From5551(ushort value)
        {
            const byte BMask = 0x1F;
            const byte GMask = 0x1F;
            const byte RMask = 0x1F;
            const byte AMask = 0x01;

            return new BgraColour
            {
                b = (byte)((0xFF / BMask) * (value & BMask)),
                g = (byte)((0xFF / GMask) * ((value >> 5) & GMask)),
                r = (byte)((0xFF / RMask) * ((value >> 10) & RMask)),
                a = (byte)((0xFF / AMask) * ((value >> 15) & AMask))
            };
        }

        public static BgraColour From4444(ushort value)
        {
            const byte BMask = 0x0F;
            const byte GMask = 0x0F;
            const byte RMask = 0x0F;
            const byte AMask = 0x0F;

            return new BgraColour
            {
                b = (byte)((0xFF / BMask) * (value & BMask)),
                g = (byte)((0xFF / GMask) * ((value >> 4) & GMask)),
                r = (byte)((0xFF / RMask) * ((value >> 8) & RMask)),
                a = (byte)((0xFF / AMask) * ((value >> 12) & AMask)),
            };
        }
    }
}
