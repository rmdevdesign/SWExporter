using SwMultiExport.Model;

namespace SwMultiExport.Exporters
{
    public interface IExporter
    {
        string Extension { get; }
        void Export(SceneData scene, string path, ExportOptions options);
    }
}
