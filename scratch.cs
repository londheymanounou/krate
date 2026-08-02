using System;
using System.Threading.Tasks;

class Program {
    static async Task Main() {
        var results = await Krate.Core.YouTube.SearchAsync("funny cats");
        foreach(var r in results) {
            Console.WriteLine(r.Title + " - " + r.Url);
        }
    }
}
