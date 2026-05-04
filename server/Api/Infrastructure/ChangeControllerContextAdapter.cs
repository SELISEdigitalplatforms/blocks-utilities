using Blocks.Genesis;

namespace Api.Infrastructure
{
    public interface IChangeControllerContext
    {
        void ChangeContext(IProjectKey request);
    }

    public sealed class ChangeControllerContextAdapter : IChangeControllerContext
    {
        private readonly ChangeControllerContext _changeControllerContext;

        public ChangeControllerContextAdapter(ChangeControllerContext changeControllerContext)
        {
            _changeControllerContext = changeControllerContext;
        }

        public void ChangeContext(IProjectKey request)
        {
            _changeControllerContext.ChangeContext(request);
        }
    }
}
