using Fluid.Values;

namespace VintageHive.Processors.LocalServer.Controllers;

internal class KitchenController : Controller
{
    [Controller("/sink.html")]
    public async Task AddSpecialSauce()
    {
        await Task.Delay(0);

        Response.Context.SetValue("fox", new StringValue("gay"));
    }

    [Controller("/add")]
    public async Task AddNumbers()
    {
        await Task.Delay(0);

        var total = Convert.ToInt32(Request.QueryParams["x"]) + Convert.ToInt32(Request.QueryParams["y"]);

        Response.SetBodyString($"{Request.QueryParams["x"]} + {Request.QueryParams["y"]} = {total}");

        Response.Handled = true;
    }
}
