using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using VMS.TPS.Common.Model.API;

namespace ESAPIScript
{
    public class EsapiWorker
    {
        private readonly Patient _p;
        private readonly IEnumerable<PlanSum> _ps;
        private readonly Dispatcher Dispatcher;

        public EsapiWorker(Patient p, IEnumerable<PlanSum> ps)
        {
            _p = p;
            _ps = ps;
            Dispatcher = Dispatcher.CurrentDispatcher;
        }

        public async Task<bool> AsyncRunPlanSumContext(Action<Patient, IEnumerable<PlanSum>> a)
        {
            await Dispatcher.BeginInvoke(a, _p, _ps);
            return true;
        }
    }
}
