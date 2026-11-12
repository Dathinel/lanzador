namespace Lanzador.Core;

/// <summary>Opciones compartidas por la ventana y la CLI; los demás argumentos se conservan en orden.</summary>
public sealed record OpcionesComunes(string Indice, string? Clave, Carpetas Carpetas, IReadOnlyList<string> Argumentos)
{
    /// <summary>La última aparición de cada opción prevalece. Una opción sin valor es un error de uso.</summary>
    public static OpcionesComunes Parsear(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string indice = Instalador.IndiceDeDathinel.AbsoluteUri;
        string? clave = null;
        Carpetas carpetas = Carpetas.PorDefecto();
        var argumentos = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is not ("--indice" or "--clave" or "--destino" or "--comun"))
            {
                argumentos.Add(arg);
                continue;
            }

            // Vacía sería la carpeta actual: «quitar x --destino ""» borraría la «x» de donde se lanzó.
            if (i + 1 >= args.Length || args[i + 1].StartsWith('-') || (arg is "--destino" or "--comun" && string.IsNullOrWhiteSpace(args[i + 1])))
            {
                throw new ErrorDeUso($"Falta el valor de {arg}.");
            }

            string valor = args[++i];
            switch (arg)
            {
                case "--indice": indice = valor; break;
                case "--clave": clave = valor; break;
                case "--destino": carpetas = carpetas with { Herramientas = valor }; break;
                case "--comun": carpetas = carpetas with { Comun = valor }; break;
            }
        }

        return new OpcionesComunes(indice, clave, carpetas, argumentos);
    }
}
