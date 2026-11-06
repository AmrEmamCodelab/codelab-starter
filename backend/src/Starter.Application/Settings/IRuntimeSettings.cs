namespace Starter.Application.Settings;

/// <summary>
/// Reads the runtime settings an administrator can change without a deployment (implemented by the Settings module).
/// Keys come from a constants class (<see cref="SettingKeys"/>, or a module's own <c>&lt;Module&gt;SettingKeys</c>), never
/// from a literal at the call site. Scoped: the first read in a scope checks the database stamp once, so a change made
/// on any node is seen by the next request; a snapshot older than <see cref="SettingsLimits.CacheTtl"/> is reloaded on
/// read, so a long-lived scope also sees a change within that bound.
/// </summary>
/// <remarks>
/// The reads are synchronous: the values come from an in-memory snapshot; at most one cheap single-row stamp query (or a
/// snapshot reload) runs per scope and per <see cref="SettingsLimits.CacheTtl"/>.
/// </remarks>
public interface IRuntimeSettings
{
    /// <summary>The value of a <see cref="SettingValueType.Text"/> setting.</summary>
    /// <exception cref="InvalidOperationException">No setting has this key, or it is not a text setting.</exception>
    string GetString(string key);

    /// <summary>The value of a <see cref="SettingValueType.WholeNumber"/> setting.</summary>
    /// <exception cref="InvalidOperationException">No setting has this key, or it is not an integer setting.</exception>
    int GetInt32(string key);

    /// <summary>The value of a <see cref="SettingValueType.Boolean"/> setting.</summary>
    /// <exception cref="InvalidOperationException">No setting has this key, or it is not a boolean setting.</exception>
    bool GetBoolean(string key);

    /// <summary>The value of a <see cref="SettingValueType.DurationSeconds"/> setting.</summary>
    /// <exception cref="InvalidOperationException">No setting has this key, or it is not a duration setting.</exception>
    TimeSpan GetDuration(string key);
}
