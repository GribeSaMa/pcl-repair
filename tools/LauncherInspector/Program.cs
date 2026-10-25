using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var md = pe.GetMetadataReader();
var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
    .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
    .ToDictionary(o => unchecked((ushort)o.Value));

string TypeName(TypeDefinitionHandle h)
{
    var t = md.GetTypeDefinition(h);
    return t.GetDeclaringType().IsNil ? md.GetString(t.Namespace) + "." + md.GetString(t.Name)
        : TypeName(t.GetDeclaringType()) + "+" + md.GetString(t.Name);
}
string TokenName(int token)
{
    try
    {
        var handle = MetadataTokens.Handle(token);
        return handle.Kind switch
        {
            HandleKind.UserString => "\"" + md.GetUserString((UserStringHandle)handle) + "\"",
            HandleKind.MethodDefinition => TypeName(md.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType()) + "::" + md.GetString(md.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
            HandleKind.FieldDefinition => TypeName(md.GetFieldDefinition((FieldDefinitionHandle)handle).GetDeclaringType()) + "::" + md.GetString(md.GetFieldDefinition((FieldDefinitionHandle)handle).Name),
            HandleKind.MemberReference => md.GetString(md.GetMemberReference((MemberReferenceHandle)handle).Name),
            HandleKind.TypeDefinition => TypeName((TypeDefinitionHandle)handle),
            HandleKind.TypeReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)handle).Name),
            _ => handle.Kind.ToString()
        };
    }
    catch { return "?"; }
}

foreach (var th in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(th);
    var tn = TypeName(th);
    foreach (var fh in td.GetFields())
    {
        var field = md.GetFieldDefinition(fh);
        var name = md.GetString(field.Name);
        if (name.Contains("Restrict", StringComparison.OrdinalIgnoreCase) || name == "DlClientListLoader")
            Console.WriteLine($"FIELD 0x{MetadataTokens.GetToken(fh):X8} {tn}::{name} attributes={field.Attributes}");
    }
    foreach (var mh in td.GetMethods())
    {
        var method = md.GetMethodDefinition(mh);
        var mn = md.GetString(method.Name);
        bool selected = args.Length > 1 ? (tn + "::" + mn).Contains(args[1], StringComparison.OrdinalIgnoreCase)
            : (tn == "PCL.PageDownloadInstall" && mn == "LoaderInit") || mn.Contains("RestrictMcDownload");
        if (method.RelativeVirtualAddress == 0) continue;
        var body = pe.GetMethodBody(method.RelativeVirtualAddress);
        if (args.Length > 1 && args[1] == "--blocks")
        {
            var raw = body.GetILBytes()!;
            selected = false;
            for (int b = 0; b + 4 < raw.Length; b++)
            {
                if (raw[b] != 0x72 || raw[b + 4] != 0x70) continue;
                var text = TokenName(BitConverter.ToInt32(raw, b + 1));
                if (text.Contains("关闭下载") || text.Contains("不再提供") || text.Contains("法律法规"))
                {
                    selected = true;
                    break;
                }
            }
        }
        if (!selected) continue;
        int fileOffset = pe.PEHeaders.SectionHeaders.Where(s => method.RelativeVirtualAddress >= s.VirtualAddress && method.RelativeVirtualAddress < s.VirtualAddress + s.SizeOfRawData)
            .Select(s => s.PointerToRawData + method.RelativeVirtualAddress - s.VirtualAddress).Single();
        Console.WriteLine($"METHOD 0x{MetadataTokens.GetToken(mh):X8} {tn}::{mn} RVA=0x{method.RelativeVirtualAddress:X} FileOffset=0x{fileOffset:X} Size={body.Size} MaxStack={body.MaxStack}");
        var il = body.GetILBytes()!;
        for (int p = 0; p < il.Length;)
        {
            int start = p;
            ushort value = il[p++];
            if (value == 0xFE) value = (ushort)(0xFE00 | il[p++]);
            var op = opcodes[value];
            int count = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, p),
                _ => 4
            };
            string operand = count == 0 ? "" : Convert.ToHexString(il.AsSpan(p, count));
            if (op.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok or OperandType.InlineString or OperandType.InlineSig)
                operand = $"0x{BitConverter.ToInt32(il, p):X8} {TokenName(BitConverter.ToInt32(il, p))}";
            else if (op.OperandType == OperandType.ShortInlineBrTarget) operand = $"IL_{p + 1 + (sbyte)il[p]:X4}";
            else if (op.OperandType == OperandType.InlineBrTarget) operand = $"IL_{p + 4 + BitConverter.ToInt32(il, p):X4}";
            Console.WriteLine($"  IL_{start:X4}: {op.Name,-14} {operand}");
            p += count;
        }
    }
}
