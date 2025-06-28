using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedlePluginLib;
public interface IReportStatistics
{
    public void ClearStatistics();
    public List<ReportFromComponent> ReportStatistics();

}
