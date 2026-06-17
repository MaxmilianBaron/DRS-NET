using System;
using DungeonRunners.Core;
using DungeonRunners.Utilities;

namespace DungeonRunners.Combat
{
    public static class UnitMover
    {
        public const int Fixed = 0x100;

        private static readonly int[] SinTable =
        {
            0, 4, 8, 13, 17, 22, 26, 31, 35, 40, 44, 48,
            53, 57, 61, 66, 70, 74, 79, 83, 87, 91, 95, 100,
            104, 108, 112, 116, 120, 124, 127, 131, 135, 139, 143, 146,
            150, 154, 157, 161, 164, 167, 171, 174, 177, 181, 184, 187,
            190, 193, 196, 198, 201, 204, 207, 209, 212, 214, 217, 219,
            221, 223, 226, 228, 230, 232, 233, 235, 237, 238, 240, 242,
            243, 244, 246, 247, 248, 249, 250, 251, 252, 252, 253, 254,
            254, 255, 255, 255, 255, 255, 256, 255, 255, 255, 255, 255,
            254, 254, 253, 252, 252, 251, 250, 249, 248, 247, 246, 244,
            243, 242, 240, 238, 237, 235, 233, 232, 230, 228, 226, 223,
            221, 219, 217, 214, 212, 209, 207, 204, 201, 198, 196, 193,
            190, 187, 184, 181, 177, 174, 171, 167, 164, 161, 157, 154,
            150, 146, 143, 139, 135, 131, 127, 124, 120, 116, 112, 108,
            104, 100, 95, 91, 87, 83, 79, 74, 70, 66, 61, 57,
            53, 48, 44, 40, 35, 31, 26, 22, 17, 13, 8, 4,
            0, -4, -8, -13, -17, -22, -26, -31, -35, -40, -44, -48,
            -53, -57, -61, -66, -70, -74, -79, -83, -87, -91, -95, -100,
            -104, -108, -112, -116, -120, -124, -128, -131, -135, -139, -143, -146,
            -150, -154, -157, -161, -164, -167, -171, -174, -177, -181, -184, -187,
            -190, -193, -196, -198, -201, -204, -207, -209, -212, -214, -217, -219,
            -221, -223, -226, -228, -230, -232, -233, -235, -237, -238, -240, -242,
            -243, -244, -246, -247, -248, -249, -250, -251, -252, -252, -253, -254,
            -254, -255, -255, -255, -255, -255, -256, -255, -255, -255, -255, -255,
            -254, -254, -253, -252, -252, -251, -250, -249, -248, -247, -246, -244,
            -243, -242, -240, -238, -237, -235, -233, -232, -230, -228, -226, -223,
            -221, -219, -217, -214, -212, -209, -207, -204, -201, -198, -196, -193,
            -190, -187, -184, -181, -177, -174, -171, -167, -164, -161, -157, -154,
            -150, -146, -143, -139, -135, -131, -128, -124, -120, -116, -112, -108,
            -104, -100, -95, -91, -87, -83, -79, -74, -70, -66, -61, -57,
            -53, -48, -44, -40, -35, -31, -26, -22, -17, -13, -8, -4,
        };

        private static readonly int[] CosTable =
        {
            256, 255, 255, 255, 255, 255, 254, 254, 253, 252, 252, 251,
            250, 249, 248, 247, 246, 244, 243, 242, 240, 238, 237, 235,
            233, 232, 230, 228, 226, 223, 221, 219, 217, 214, 212, 209,
            207, 204, 201, 198, 196, 193, 190, 187, 184, 181, 177, 174,
            171, 167, 164, 161, 157, 154, 150, 146, 143, 139, 135, 131,
            128, 124, 120, 116, 112, 108, 104, 100, 95, 91, 87, 83,
            79, 74, 70, 66, 61, 57, 53, 48, 44, 40, 35, 31,
            26, 22, 17, 13, 8, 4, 0, -4, -8, -13, -17, -22,
            -26, -31, -35, -40, -44, -48, -53, -57, -61, -66, -70, -74,
            -79, -83, -87, -91, -95, -100, -104, -108, -112, -116, -120, -124,
            -127, -131, -135, -139, -143, -146, -150, -154, -157, -161, -164, -167,
            -171, -174, -177, -181, -184, -187, -190, -193, -196, -198, -201, -204,
            -207, -209, -212, -214, -217, -219, -221, -223, -226, -228, -230, -232,
            -233, -235, -237, -238, -240, -242, -243, -244, -246, -247, -248, -249,
            -250, -251, -252, -252, -253, -254, -254, -255, -255, -255, -255, -255,
            -256, -255, -255, -255, -255, -255, -254, -254, -253, -252, -252, -251,
            -250, -249, -248, -247, -246, -244, -243, -242, -240, -238, -237, -235,
            -233, -232, -230, -228, -226, -223, -221, -219, -217, -214, -212, -209,
            -207, -204, -201, -198, -196, -193, -190, -187, -184, -181, -177, -174,
            -171, -167, -164, -161, -157, -154, -150, -146, -143, -139, -135, -131,
            -128, -124, -120, -116, -112, -108, -104, -100, -95, -91, -87, -83,
            -79, -74, -70, -66, -61, -57, -53, -48, -44, -40, -35, -31,
            -26, -22, -17, -13, -8, -4, 0, 4, 8, 13, 17, 22,
            26, 31, 35, 40, 44, 48, 53, 57, 61, 66, 70, 74,
            79, 83, 87, 91, 95, 100, 104, 108, 112, 116, 120, 124,
            128, 131, 135, 139, 143, 146, 150, 154, 157, 161, 164, 167,
            171, 174, 177, 181, 184, 187, 190, 193, 196, 198, 201, 204,
            207, 209, 212, 214, 217, 219, 221, 223, 226, 228, 230, 232,
            233, 235, 237, 238, 240, 242, 243, 244, 246, 247, 248, 249,
            250, 251, 252, 252, 253, 254, 254, 255, 255, 255, 255, 255,
        };

        private static readonly int[] SquareRootTable = InitSquareRootTable();

        private static int[] InitSquareRootTable()
        {
            var table = new int[0x100];
            for (int i = 0; i < 0x100; i++)
                table[i] = (int)Math.Round(Math.Sqrt(i * (1.0 / 255.0)) * 65535.0, MidpointRounding.ToEven);
            return table;
        }

        public static int TableSquareRoot(uint value)
        {
            if ((int)value <= 0) return 0;
            int shift = 0x1f;
            if ((value >> 6) != 0)
            {
                while (((value >> 6) >> shift) == 0) shift--;
            }
            return (int)(((uint)(SquareRootTable[value >> (shift & 0x1e)] << (shift >> 1 & 0x1f)) + 1) >> 8);
        }

        public static int IntSqrt(long value)
        {
            if (value <= 0) return 0;
            long x = 0;
            long bit = 1L << 62;
            while (bit > value) bit >>= 2;
            while (bit != 0)
            {
                if (value >= x + bit)
                {
                    value -= x + bit;
                    x = (x >> 1) + bit;
                }
                else
                {
                    x >>= 1;
                }
                bit >>= 2;
            }
            return (int)x;
        }

        public static int WrapDegrees(int deg)
        {
            deg %= 360;
            if (deg < 0) deg += 360;
            return deg;
        }

        public static float ZRotateCos(int degrees) => CosTable[WrapDegrees(degrees)] / 256f;
        public static float ZRotateSin(int degrees) => SinTable[WrapDegrees(degrees)] / 256f;

        public const int FullCircleFixed = 0x16800;

        public static int TurnRatePerTickFixed(int turnRateDegrees)
        {
            return (turnRateDegrees << 8) / 30;
        }

        public static int InterpolateHeading(int current, int target, int turnRate)
        {
            int diff = target - current;
            if (diff >= 0xB401) diff -= FullCircleFixed;
            else if (diff < -0xB400) diff += FullCircleFixed;
            int result;
            if (diff < 0)
                result = -diff < turnRate ? target : current - turnRate;
            else if (diff == 0)
                result = current;
            else
                result = diff < turnRate ? target : current + turnRate;
            result %= FullCircleFixed;
            if (result < 0) result += FullCircleFixed;
            return result;
        }

        private static readonly int[] AsinTable = BuildAsinAcosTable(true);
        private static readonly int[] AcosTable = BuildAsinAcosTable(false);

        private static int[] BuildAsinAcosTable(bool asin)
        {
            var table = new int[0x201];
            for (int tableIndex = 0; tableIndex <= 0x200; tableIndex++)
            {
                double input = (tableIndex - 0x100) / 256.0;
                if (input < -1.0) input = -1.0; else if (input > 1.0) input = 1.0;
                double radians = asin ? Math.Asin(input) : Math.Acos(input);
                table[tableIndex] = (int)Math.Round(radians * 180.0 / Math.PI * 256.0);
            }
            return table;
        }

        public static int VectorToHeadingFixed(int dxFixed, int dyFixed)
        {
            if (dxFixed == 0 && dyFixed == 0) return 0;
            int xSq = (int)(((long)dxFixed * dxFixed) >> 8);
            int ySq = (int)(((long)dyFixed * dyFixed) >> 8);
            int sq = TableSquareRoot((uint)(xSq + ySq));
            if (sq == 0) return 0;
            int h;
            if (Math.Abs((long)dyFixed) < Math.Abs((long)dxFixed))
            {
                int ratio = (int)(((long)dyFixed << 8) / sq) + 0x100;
                if (ratio < 0) ratio = 0; else if (ratio > 0x200) ratio = 0x200;
                if (dxFixed < 0) h = (AsinTable[ratio] >> 8) + 0x10e;
                else h = 0x5a - (AsinTable[ratio] >> 8);
            }
            else
            {
                int ratio = (int)(((long)dxFixed << 8) / sq) + 0x100;
                if (ratio < 0) ratio = 0; else if (ratio > 0x200) ratio = 0x200;
                if (dyFixed < 0) h = (AcosTable[ratio] >> 8) + 0x5a;
                else h = 0x5a - (AcosTable[ratio] >> 8);
            }
            int deg = ((0x168 - h) % 0x168 + 0x168) % 0x168;
            return deg << 8;
        }

        public static void StepTowardFixedHeading(int curX, int curY, int headingCur, int tgtX, int tgtY, int stepFixed, int turnRate, out int newX, out int newY, out int newHeading, out bool arrived)
        {
            long dx = (long)tgtX - curX;
            long dy = (long)tgtY - curY;
            if (dx == 0 && dy == 0)
            {
                newX = tgtX; newY = tgtY; newHeading = headingCur; arrived = true;
                return;
            }
            int targetHeading = VectorToHeadingFixed((int)dx, (int)dy);
            newHeading = InterpolateHeading(headingCur, targetHeading, turnRate);
            var (fvx, fvy) = VectorType2D.FromHeading(new Fixed32(newHeading));
            var (tvx, tvy) = VectorType2D.FromHeading(new Fixed32(targetHeading));
            int fx = fvx.RawValue;
            int fy = fvy.RawValue;
            int tx = tvx.RawValue;
            int ty = tvy.RawValue;
            int dot = (fx * tx + fy * ty) >> 8;
            if (dot <= 0xE5)
            {
                newX = curX; newY = curY; arrived = false;
                return;
            }
            int dist = IntSqrt(dx * dx + dy * dy);
            if (dist <= stepFixed)
            {
                newX = tgtX; newY = tgtY; arrived = true;
                return;
            }
            newX = curX + (int)(((long)fx * stepFixed) >> 8);
            newY = curY + (int)(((long)fy * stepFixed) >> 8);
            arrived = false;
        }

        public static void ResolveMovement(PathMap pathMap, int curFixedX, int curFixedY, int candFixedX, int candFixedY, out int outFixedX, out int outFixedY)
        {
            if (pathMap == null) { outFixedX = candFixedX; outFixedY = candFixedY; return; }
            float curWX = curFixedX / (float)Fixed;
            float curWY = curFixedY / (float)Fixed;
            float candWX = candFixedX / (float)Fixed;
            float candWY = candFixedY / (float)Fixed;
            if (!pathMap.CastGroundRaySlide(curWX, curWY, candWX, candWY, out float slidX, out float slidY))
            {
                outFixedX = candFixedX; outFixedY = candFixedY;
                return;
            }
            outFixedX = (int)Math.Round(slidX * Fixed);
            outFixedY = (int)Math.Round(slidY * Fixed);
        }
    }
}
