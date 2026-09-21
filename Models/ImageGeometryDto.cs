namespace SFRThelper.Models
{
    public class ImageGeometryDto
    {
        public double OriginX { get; set; }
        public double OriginY { get; set; }
        public double OriginZ { get; set; }
        public double XRes { get; set; }
        public double YRes { get; set; }
        public double ZRes { get; set; }
        public int XSize { get; set; }
        public int YSize { get; set; }
        public int ZSize { get; set; }

        public double GetSliceZ(int sliceIndex)
        {
            return OriginZ + sliceIndex * ZRes;
        }

        public int GetNearestSliceIndex(double z)
        {
            if (ZRes == 0 || ZSize <= 0)
                return 0;

            int index = (int)System.Math.Round((z - OriginZ) / ZRes);
            if (index < 0) index = 0;
            if (index >= ZSize) index = ZSize - 1;
            return index;
        }
    }
}
