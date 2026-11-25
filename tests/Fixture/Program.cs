using Vmf.ManagedCoverage;

var input = Console.OpenStandardInput().ReadByte();
var result = Classifier.Classify(input);
EdgeCoverage.WriteTo(args[0]);
Console.WriteLine(result);
