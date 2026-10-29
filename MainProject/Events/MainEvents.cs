namespace ResilientTranslator.MainProject.Events
{
    /// <summary>Publishes shared application notifications such as immutable palette changes.</summary>
    public static class MainEvents
    {
        const bool IsLogEnabled = true;
        public static event Action<string, object?>? OnDataChanged;

        /// <summary>Publishes one named application notification and its optional payload to current subscribers.</summary>
        /// <param name="eventName">The existing shared application event name to publish.</param>
        /// <param name="data">The callback payload supplied by its Windows/runtime event or application publisher.</param>
        public static void Trigger(string eventName, object? data = null)
        {
            Log($"Application notification; event={eventName}; hasPayload={data is not null}",Run:IsLogEnabled);
            foreach(var subscriber in OnDataChanged?.GetInvocationList() ?? [])
                ResilientTranslator.MainProject.Infrastructure.ExceptionBoundary.RunSafely(() => ((Action<string,object?>)subscriber)(eventName,data),operationName:$"Notify-{eventName}");
        }
    }
}
