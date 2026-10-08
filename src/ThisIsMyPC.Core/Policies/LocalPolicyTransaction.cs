using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Policies;

/// <summary>A locked local computer policy session. Saving must reject concurrent file changes.</summary>
public interface ILocalPolicySession : IDisposable
{
    PolicySourceSnapshot ReadSource();
    void WriteSaved(string location, ChangeValueType type, LocalPolicyValue value);
    void Save();
}

/// <summary>Coordinates saved and live values. Restores both when a write or verification fails.</summary>
public static class LocalPolicyTransaction
{
    public static OperationResult<bool> Apply(ILocalPolicySession session, IRegistryService registry, string location,
        ChangeValueType type, LocalPolicyValue before, LocalPolicyValue after)
    {
        var split = location.LastIndexOf('\\');
        var key = location[..split];
        var name = location[(split + 1)..];
        LocalPolicyValue Read() => LocalPolicyValue.Read(location, type, [session.ReadSource()], registry.ReadValue(key, name));
        void WriteLive(string value)
        {
            var result = value.Length == 0 ? registry.DeleteValue(key, name)
                : LocalPolicyValue.RegistryType(type) == ChangeValueType.Registry_String ? registry.WriteString(key, name, value)
                : registry.WriteDWord(key, name, int.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
            if (!result.IsSuccess && !(value.Length == 0 && result.ErrorCategory == ErrorCategory.NotFound))
                throw new InvalidOperationException(result.ErrorMessage ?? "Windows rejected the policy value.");
        }

        try
        {
            if (Read() != before) return OperationResult<bool>.Failure("The saved or current policy changed. Refresh before applying.", ErrorCategory.ProtectedByPolicy);
            if (before == after) return OperationResult<bool>.Success(true);
        }
        catch (Exception ex) { return OperationResult<bool>.Failure(ex.Message, ErrorCategory.ProtectedByPolicy, ex); }

        try
        {
            session.WriteSaved(location, type, after);
            session.Save();
            // Saving can trigger policy processing. Accept only our expected value or the captured live value.
            var saved = Read();
            if (saved.Saved != after.Saved || saved.Delete != after.Delete || (saved.Live != before.Live && saved.Live != after.Live))
                throw new InvalidOperationException("Policy state changed during Apply.");
            WriteLive(after.Live);
            if (Read() != after) throw new InvalidOperationException("Windows did not retain the saved and current policy values.");
            return OperationResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            try
            {
                var current = Read();
                if ((current.Saved != before.Saved || current.Delete != before.Delete)
                    && (current.Saved != after.Saved || current.Delete != after.Delete))
                    throw new InvalidOperationException("Another writer changed the saved policy.");
                if (current.Live != before.Live && current.Live != after.Live)
                    throw new InvalidOperationException("Another writer changed the current policy.");
                session.WriteSaved(location, type, before);
                session.Save();
                WriteLive(before.Live);
                if (Read() != before) throw new InvalidOperationException("The previous policy values could not be verified.");
                return OperationResult<bool>.Failure(ex.Message + " The previous policy values were restored.", ErrorCategory.ProtectedByPolicy, ex);
            }
            catch (Exception rollback)
            {
                return OperationResult<bool>.Failure(ex.Message + " Restoration is incomplete: " + rollback.Message,
                    ErrorCategory.ServiceUnavailable, rollback);
            }
        }
    }
}
