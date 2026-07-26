using System;
using System.Reflection;

internal static class Net20ExceptionRelayProbe
{
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine(
                "Usage: Net20ExceptionRelayProbe <CUETools.Codecs net20 assembly>");
            return 2;
        }

        try
        {
            Assembly assembly = Assembly.LoadFrom(args[0]);
            Type relay = assembly.GetType(
                "CUETools.Codecs.ExceptionRelay", true);
            MethodInfo throwMethod = relay.GetMethod(
                "Throw",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (throwMethod == null)
                throw new MissingMethodException(relay.FullName, "Throw");

            var original = new InvalidOperationException(
                "net20 exception relay compatibility probe");
            try
            {
                throwMethod.Invoke(null, new object[] { original });
                Console.Error.WriteLine(
                    "FAIL: ExceptionRelay.Throw returned normally.");
                return 1;
            }
            catch (TargetInvocationException ex)
            {
                if (!Object.ReferenceEquals(ex.InnerException, original))
                {
                    Console.Error.WriteLine(
                        "FAIL: net20 changed the relayed exception from {0} to {1}.",
                        original.GetType().FullName,
                        ex.InnerException == null
                            ? "<null>"
                            : ex.InnerException.GetType().FullName);
                    return 1;
                }
            }

            Console.WriteLine(
                "PASS: net20 preserved relayed exception type and identity.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                "FAIL: net20 exception relay probe could not run: {0}: {1}",
                ex.GetType().FullName,
                ex.Message);
            return 1;
        }
    }
}
