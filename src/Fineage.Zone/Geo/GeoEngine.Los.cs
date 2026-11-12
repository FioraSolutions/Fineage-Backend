namespace Fineage.Zone.Geo;

public sealed partial class GeoEngine
{
    // ---- Mobius GeoEngine.canSeeTarget (LOS) ----
    const int ElevatedSeeOverDistance = 2;
    const int MaxSeeOverHeight = 48;

    int NearestZ(int gx, int gy, int z) => CellAt(gx, gy, z, out int h, out _) ? h : z;

    bool HasGeoPos(int gx, int gy) => RegionAtGeo(gx, gy, out _, out _) != null;

    bool CheckNswe(int gx, int gy, int z, byte dir)
    {
        CellAt(gx, gy, z, out _, out byte n);
        return (n & dir) == dir;
    }

    int NextHigherZ(int gx, int gy, int z)
    {
        var r = RegionAtGeo(gx, gy, out int lx, out int ly);
        return r == null ? z : r.NextHigher(lx, ly, z);
    }

    static byte ComputeNswe(int lastX, int lastY, int x, int y)
    {
        byte n = 0;
        if (x > lastX) n |= East; else if (x < lastX) n |= West;
        if (y > lastY) n |= South; else if (y < lastY) n |= North;
        return n;
    }

    bool AntiCornerCut(int gx, int gy, int z, byte nswe)
    {
        bool ok = true;
        if ((nswe & (North | East)) == (North | East))
            ok = CheckNswe(gx, gy - 1, z, East) && CheckNswe(gx + 1, gy, z, North);
        if (ok && (nswe & (North | West)) == (North | West))
            ok = CheckNswe(gx, gy - 1, z, West) && CheckNswe(gx - 1, gy, z, North);
        if (ok && (nswe & (South | East)) == (South | East))
            ok = CheckNswe(gx, gy + 1, z, East) && CheckNswe(gx + 1, gy, z, South);
        if (ok && (nswe & (South | West)) == (South | West))
            ok = CheckNswe(gx, gy + 1, z, West) && CheckNswe(gx - 1, gy, z, South);
        return ok && CheckNswe(gx, gy, z, nswe);
    }

    int LosGeoZ(int px, int py, int pz, int cx, int cy, byte nswe) =>
        AntiCornerCut(px, py, pz, nswe) ? NearestZ(cx, cy, pz) : NextHigherZ(cx, cy, pz);

    /// <summary>
    /// Port of Mobius <c>GeoEngine.canSeeTarget(x,y,z,tx,ty,tz)</c>: 3D grid line from the higher end; each cell's LOS Z
    /// (nearest if passable, else next higher layer) must stay under the eye line + 48 (from the start's Z for the first 2 cells).
    /// Cells without geo are see-through.
    /// </summary>
    public bool CanSeeTarget(int x, int y, int z, int tx, int ty, int tz)
    {
        int gx = GeoX(x), gy = GeoY(y), tgx = GeoX(tx), tgy = GeoY(ty);
        int fromZ = NearestZ(gx, gy, z), toZ = NearestZ(tgx, tgy, tz);
        if (gx == tgx && gy == tgy)
            return !HasGeoPos(tgx, tgy) || fromZ == toZ;
        if (toZ > fromZ)
        {
            (gx, tgx) = (tgx, gx);
            (gy, tgy) = (tgy, gy);
            (fromZ, toZ) = (toZ, fromZ);
        }

        var it = new GridLine3D(gx, gy, fromZ, tgx, tgy, toZ);
        it.Next();
        int px = it.X, py = it.Y, pz = it.Z, index = 0;
        while (it.Next())
        {
            int cx = it.X, cy = it.Y;
            if (cx == px && cy == py)
                continue;
            int beeZ = it.Z;
            int cz = pz;
            if (HasGeoPos(cx, cy))
            {
                byte nswe = ComputeNswe(px, py, cx, cy);
                cz = LosGeoZ(px, py, pz, cx, cy, nswe);
                int maxHeight = index < ElevatedSeeOverDistance ? fromZ + MaxSeeOverHeight : beeZ + MaxSeeOverHeight;
                bool through = false;
                if (cz <= maxHeight)
                {
                    through = true;
                    int ny = (nswe & North) != 0 ? py - 1 : (nswe & South) != 0 ? py + 1 : py;
                    int nx = (nswe & East) != 0 ? px + 1 : (nswe & West) != 0 ? px - 1 : px;
                    if (nx != px && ny != py)
                    {
                        byte vDir = (nswe & North) != 0 ? North : South;
                        byte hDir = (nswe & East) != 0 ? East : West;
                        int vz = LosGeoZ(px, py, pz, px, ny, vDir);
                        int hz = LosGeoZ(px, py, pz, nx, py, hDir);
                        through = vz <= maxHeight && hz <= maxHeight && vz <= NearestZ(px, ny, beeZ) && hz <= NearestZ(nx, py, beeZ);
                    }
                }
                if (!through)
                    return false;
            }
            px = cx;
            py = cy;
            pz = cz;
            index++;
        }
        return true;
    }

    /// <summary>Mobius GridLineIterator3D (3D Bresenham over geo X/Y and world Z).</summary>
    struct GridLine3D
    {
        int _x, _y, _z, _errXY, _errXZ;
        readonly int _tx, _ty, _tz, _dx, _dy, _dz, _sx, _sy, _sz;
        bool _started;

        public GridLine3D(int x, int y, int z, int tx, int ty, int tz)
        {
            (_x, _y, _z, _tx, _ty, _tz) = (x, y, z, tx, ty, tz);
            _dx = Math.Abs(tx - x); _dy = Math.Abs(ty - y); _dz = Math.Abs(tz - z);
            _sx = x < tx ? 1 : -1; _sy = y < ty ? 1 : -1; _sz = z < tz ? 1 : -1;
            int major = _dx >= _dy && _dx >= _dz ? _dx : _dy >= _dx && _dy >= _dz ? _dy : _dz;
            _errXY = _errXZ = major / 2;
            _started = false;
        }

        public readonly int X => _x;
        public readonly int Y => _y;
        public readonly int Z => _z;

        public bool Next()
        {
            if (!_started) { _started = true; return true; }
            if (_x == _tx && _y == _ty && _z == _tz) return false;
            if (_dx >= _dy && _dx >= _dz)
            {
                _x += _sx;
                _errXY += _dy; if (_errXY >= _dx) { _y += _sy; _errXY -= _dx; }
                _errXZ += _dz; if (_errXZ >= _dx) { _z += _sz; _errXZ -= _dx; }
            }
            else if (_dy >= _dx && _dy >= _dz)
            {
                _y += _sy;
                _errXY += _dx; if (_errXY >= _dy) { _x += _sx; _errXY -= _dy; }
                _errXZ += _dz; if (_errXZ >= _dy) { _z += _sz; _errXZ -= _dy; }
            }
            else
            {
                _z += _sz;
                _errXY += _dx; if (_errXY >= _dz) { _x += _sx; _errXY -= _dz; }
                _errXZ += _dy; if (_errXZ >= _dz) { _y += _sy; _errXZ -= _dz; }
            }
            return true;
        }
    }
}
