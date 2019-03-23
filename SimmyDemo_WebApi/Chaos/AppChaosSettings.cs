using System.Collections.Generic;
using System.Linq;

namespace SimmyDemo_WebApi.Chaos
{
    public class AppChaosSettings
    {
        public List<OperationChaosSetting> OperationChaosSettings { get; set; }

        public OperationChaosSetting GetSettingsFor(string operationKey) => OperationChaosSettings?.SingleOrDefault(i => i.OperationKey == operationKey);
    }
}
