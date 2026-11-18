using UnityEngine;

namespace Calculatorio
{
    [DisallowMultipleComponent]
    public class Gfx : MonoBehaviour
    {
        static Material _mat;
        static void EnsureMat()
        {
            if (_mat) return;
            var sh = Shader.Find("Hidden/Internal-Colored");
            _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _mat.SetInt("_ZWrite", 0);
        }

        public static void RectFill(Rect r, Color c)
        {
            EnsureMat();
            _mat.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            GL.Begin(GL.QUADS);
            GL.Color(c);
            GL.Vertex3(r.xMin, r.yMin, 0);
            GL.Vertex3(r.xMax, r.yMin, 0);
            GL.Vertex3(r.xMax, r.yMax, 0);
            GL.Vertex3(r.xMin, r.yMax, 0);
            GL.End();
            GL.PopMatrix();
        }
        public static void RectFrame(Rect r, Color c, float t=1f)
        {
            RectFill(new Rect(r.xMin, r.yMin, r.width, t), c);
            RectFill(new Rect(r.xMin, r.yMax-t, r.width, t), c);
            RectFill(new Rect(r.xMin, r.yMin, t, r.height), c);
            RectFill(new Rect(r.xMax-t, r.yMin, t, r.height), c);
        }
        public static void Bar(Rect r, float v01, Color fill, Color bg)
        {
            RectFill(r, bg);
            var f = new Rect(r.x, r.y + r.height*(1f - Mathf.Clamp01(v01)), r.width, r.height*Mathf.Clamp01(v01));
            RectFill(f, fill);
            RectFrame(r, new Color(1,1,1,0.2f), 1f);
        }
    }
}
