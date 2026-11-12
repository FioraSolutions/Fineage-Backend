namespace Fineage.Zone.Geo;

public sealed partial class GeoEngine
{
    internal sealed class Region
    {
        // Per block: offset into _cells (packed short: height<<4 | nswe like L2J) and layer index for multilayer.
        readonly byte[] _type = new byte[RegionBlocks * RegionBlocks];
        readonly int[] _offset = new int[RegionBlocks * RegionBlocks];
        readonly List<short> _data = [];
        short[] _arr = [];

        const byte Flat = 0, Complex = 1, Multi = 2;

        internal static Region Flat1(short height)
        {
            var r = new Region();
            for (int i = 0; i < r._type.Length; i++)
            {
                r._type[i] = Flat;
                r._offset[i] = 0;
            }

            r._data.Add(height);
            r._arr = [.. r._data];
            return r;
        }

        /// <summary>Test hook: overwrite one block as complex with given per-cell (height, nswe).</summary>
        internal void SetComplexBlock(int bx, int by, Func<int, int, (short H, byte Nswe)> cell)
        {
            int b = bx * RegionBlocks + by;
            _type[b] = Complex;
            var list = new List<short>(_arr);
            _offset[b] = list.Count;
            for (int cx = 0; cx < 8; cx++)
            for (int cy = 0; cy < 8; cy++)
            {
                var (h, n) = cell(cx, cy);
                list.Add(Pack(h, n));
            }

            _arr = [.. list];
        }

        static short Pack(short h, byte nswe) => (short)(((h << 1) & 0xFFF0) | (nswe & 0x0F));

        internal static Region ParseL2j(byte[] b)
        {
            var r = new Region();
            int p = 0;
            for (int bx = 0; bx < RegionBlocks; bx++)
            for (int by = 0; by < RegionBlocks; by++)
            {
                int idx = bx * RegionBlocks + by;
                byte t = b[p++];
                r._type[idx] = t;
                r._offset[idx] = r._data.Count;
                switch (t)
                {
                    case Flat:
                        r._data.Add(BitConverter.ToInt16(b, p));
                        p += 2;
                        break;
                    case Complex:
                        for (int i = 0; i < 64; i++, p += 2)
                            r._data.Add(BitConverter.ToInt16(b, p));
                        break;
                    case Multi:
                        for (int i = 0; i < 64; i++)
                        {
                            byte layers = b[p++];
                            r._data.Add(layers);
                            for (int l = 0; l < layers; l++, p += 2)
                                r._data.Add(BitConverter.ToInt16(b, p));
                        }

                        break;
                    default:
                        throw new InvalidDataException($"bad l2j block type {t} at {p - 1}");
                }
            }

            r._arr = [.. r._data];
            r._data.Clear();
            return r;
        }

        /// <summary>aCis L2D: 0xD0 flat(short h), 0xD1 complex(64×[byte nswe, short h]), 0xD2 multi(64×[byte n, n×[byte nswe, short h]]).</summary>
        internal static Region ParseL2d(byte[] b)
        {
            var r = new Region();
            int p = 0;
            for (int bx = 0; bx < RegionBlocks; bx++)
            for (int by = 0; by < RegionBlocks; by++)
            {
                int idx = bx * RegionBlocks + by;
                byte t = b[p++];
                r._offset[idx] = r._data.Count;
                switch (t)
                {
                    case 0xD0:
                        r._type[idx] = Flat;
                        r._data.Add(BitConverter.ToInt16(b, p));
                        p += 2;
                        break;
                    case 0xD1:
                        r._type[idx] = Complex;
                        for (int i = 0; i < 64; i++)
                        {
                            byte n = b[p++];
                            short h = BitConverter.ToInt16(b, p);
                            p += 2;
                            r._data.Add(Pack(h, n));
                        }

                        break;
                    case 0xD2:
                        r._type[idx] = Multi;
                        for (int i = 0; i < 64; i++)
                        {
                            byte layers = b[p++];
                            r._data.Add(layers);
                            for (int l = 0; l < layers; l++)
                            {
                                byte n = b[p++];
                                short h = BitConverter.ToInt16(b, p);
                                p += 2;
                                r._data.Add(Pack(h, n));
                            }
                        }

                        break;
                    default:
                        throw new InvalidDataException($"bad l2d block type 0x{t:X2} at {p - 1}");
                }
            }

            r._arr = [.. r._data];
            r._data.Clear();
            return r;
        }

        static short Height(short packed) => (short)((short)(packed & 0xFFF0) >> 1);

        /// <summary>Mobius getNextHigherZ: flat/complex → max(height, z); multilayer → lowest layer ≥ z, else z.</summary>
        internal int NextHigher(int lx, int ly, int z)
        {
            int b = (lx >> 3) * RegionBlocks + (ly >> 3);
            int off = _offset[b];
            int cell = ((lx & 7) << 3) + (ly & 7);
            switch (_type[b])
            {
                case Flat:
                    return _arr[off] >= z ? _arr[off] : z;
                case Complex:
                {
                    int h = Height(_arr[off + cell]);
                    return h >= z ? h : z;
                }
                default:
                {
                    int p = off;
                    for (int i = 0; i < cell; i++)
                        p += 1 + _arr[p];
                    int layers = _arr[p++];
                    int higher = int.MaxValue;
                    for (int l = 0; l < layers; l++)
                    {
                        int h = Height(_arr[p + l]);
                        if (h == z) return h;
                        if (h > z && h < higher) higher = h;
                    }
                    return higher == int.MaxValue ? z : higher;
                }
            }
        }

        internal void Nearest(int lx, int ly, int z, out short height, out byte nswe)
        {
            int b = (lx >> 3) * RegionBlocks + (ly >> 3);
            int off = _offset[b];
            int cell = ((lx & 7) << 3) + (ly & 7);
            switch (_type[b])
            {
                case Flat:
                    height = _arr[off];
                    nswe = All;
                    return;
                case Complex:
                {
                    short v = _arr[off + cell];
                    height = Height(v);
                    nswe = (byte)(v & 0x0F);
                    return;
                }
                default:
                {
                    int p = off;
                    for (int i = 0; i < cell; i++)
                        p += 1 + _arr[p];
                    int layers = _arr[p++];
                    height = (short)z;
                    nswe = All;
                    int best = int.MaxValue;
                    for (int l = 0; l < layers; l++)
                    {
                        short v = _arr[p + l];
                        short h = Height(v);
                        int d = Math.Abs(h - z);
                        if (d < best)
                        {
                            best = d;
                            height = h;
                            nswe = (byte)(v & 0x0F);
                        }
                    }

                    return;
                }
            }
        }
    }
}
