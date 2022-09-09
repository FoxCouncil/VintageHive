using Fluid;

namespace VintageHive.Processors.LocalServer.Controllers;

internal class AdminController : Controller
{
    [Controller("/index.html")]
    public async Task Index()
    {
        await Task.Delay(0);

        if (!HasSession("count"))
        {
            Session.count = 1;
        }
        else
        {
            Session.count += 1;
        }        

        Response.Context.SetValue("now", DateTime.Now);
    }
}
