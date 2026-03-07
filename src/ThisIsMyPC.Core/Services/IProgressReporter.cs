namespace ThisIsMyPC.Core.Services;

public interface IProgressReporter
{
    void ReportProgress(string message, double? percentComplete);
}
