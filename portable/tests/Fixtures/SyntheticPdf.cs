using System.Text;
namespace NeeView.Tests;
/// <summary>独立测试用PDF：方向颜色、CropBox/旋转与两层目录，不使用用户文档或外部生成工具。</summary>
public sealed class SyntheticPdf : IDisposable
{
    public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "neeview-pdf-test-" + Guid.NewGuid().ToString("N"));
    public string Path => System.IO.Path.Combine(Root, "测试文档.pdf");
    public SyntheticPdf()
    {
        Directory.CreateDirectory(Root);
        var a = "1 0 0 rg 0 200 200 100 re f 0 1 0 rg 0 0 200 100 re f";
        var b = "0 0 1 rg 100 100 300 200 re f";
        var objects = new[] {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 7 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] /Resources << >> /Contents 5 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /CropBox [100 100 400 300] /Rotate 90 /Resources << >> /Contents 6 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(a)} >>\nstream\n{a}\nendstream",
            $"<< /Length {Encoding.ASCII.GetByteCount(b)} >>\nstream\n{b}\nendstream",
            "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 2 >>",
            "<< /Title (Chapter one) /Parent 7 0 R /Dest [3 0 R /Fit] /First 9 0 R /Last 9 0 R /Count 1 >>",
            "<< /Title (Rotated page) /Parent 8 0 R /Dest [4 0 R /Fit] >>",
            "<< /CreationDate (D:20261006030000Z) /ModDate (D:20261006040000Z) >>",
        };
        using var output = new MemoryStream(); var offsets = new List<long>();
        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.4\n");
        for (var i = 0; i < objects.Length; i++) { offsets.Add(output.Position); Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = output.Position; Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R /Info 10 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllBytes(Path, output.ToArray());
    }
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}
