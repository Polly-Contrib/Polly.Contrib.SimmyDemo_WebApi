using Polly;

namespace SimmyDemo_WebApi.Chaos
{
    public static class SimmyContextExtensions
    {
        public const string ChaosSettings = "ChaosSettings";

        public static Context WithChaosSettings(this Context context, AppChaosSettings options)
        {
            context[ChaosSettings] = options;
            return context;
        }

        public static AppChaosSettings GetChaosSettings(this Context context) => context.GetSetting<AppChaosSettings>(ChaosSettings);

        private static T GetSetting<T>(this Context context, string key)
        {
            if (context.TryGetValue(key, out object setting))
            {
                if (setting is T)
                {
                    return (T)setting;
                }
            }
            return default(T);
        }

    }
}
