// Progress reporting interface
public interface IProgressReporter
{
    void ReportProgress(string message, double? percentage = null);
    void ReportError(string error);
    void ReportCompletion(string message);
    void ReportInfo(string info);
}