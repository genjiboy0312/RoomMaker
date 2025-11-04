//using UnityEngine;
//using OpenCVForUnity.CoreModule;

//public static class OpenCVTextureUtils
//{
//    public static Mat Texture2DToMat(Texture2D tex)
//    {
//        Color32[] pixels = tex.GetPixels32();
//        Mat mat = new Mat(tex.height, tex.width, CvType.CV_8UC4);
//        mat.put(0, 0, Color32ArrayToByteArray(pixels));
//        return mat;
//    }

//    public static Texture2D MatToTexture2D(Mat mat)
//    {
//        int width = mat.cols();
//        int height = mat.rows();
//        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
//        byte[] data = new byte[width * height * 4];
//        mat.get(0, 0, data);
//        tex.LoadRawTextureData(data);
//        tex.Apply();
//        return tex;
//    }

//    private static byte[] Color32ArrayToByteArray(Color32[] colors)
//    {
//        int length = colors.Length;
//        byte[] bytes = new byte[length * 4];
//        for (int i = 0; i < length; i++)
//        {
//            bytes[i * 4] = colors[i].r;
//            bytes[i * 4 + 1] = colors[i].g;
//            bytes[i * 4 + 2] = colors[i].b;
//            bytes[i * 4 + 3] = colors[i].a;
//        }
//        return bytes;
//    }
//}
