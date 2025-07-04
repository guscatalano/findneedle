using FindNeedlePluginLib;

namespace FindNeedlePluginLib.Interfaces;

public interface IReportProgress
{
    void SetProgressSink(SearchProgressSink sink);
}
