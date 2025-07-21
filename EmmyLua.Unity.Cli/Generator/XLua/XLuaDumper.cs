using System.Text;
using Microsoft.CodeAnalysis;

namespace EmmyLua.Unity.Generator.XLua;

public class XLuaDumper : IDumper
{
    public string Name => "XLuaDumper";

    // 500kb
    private static readonly int SingleFileLength = 500 * 1024;

    private int Count { get; set; } = 0;

    private Dictionary<string, bool> NamespaceDict { get; } = new ();

    public void Dump(List<CSType> csTypes, string outPath)
    {
        if (!Directory.Exists(outPath))
        {
            Directory.CreateDirectory(outPath);
        }
        Dictionary<string, bool> fullClassDic  = new ();
        List<CSType> types = new List<CSType>();
        foreach (CSType type in csTypes)
        {
            string fullClass = type.Namespace + type.Name;
            if (fullClassDic.ContainsKey(fullClass))
            {
                continue;
            }
            fullClassDic[fullClass] = true;
            types.Add(type);
        }
        HandleNamespace(types);
        var sb = new StringBuilder();
        ResetSb(sb);
        foreach (var csType in types)
        {
            
            switch (csType)
            {
                case CSClassType csClassType:
                    HandleCsClassType(csClassType, sb);
                    break;
                case CSInterface csInterface:
                    HandleCsInterface(csInterface, sb);
                    break;
                case CSEnumType csEnumType:
                    HandleCsEnumType(csEnumType, sb);
                    break;
                case CSDelegate csDelegate:
                    HandleCsDelegate(csDelegate, sb);
                    break;
            }

            sb.AppendLine();

            CacheOrDumpToFile(sb, outPath);
        }

        if (sb.Length > 0)
        {
            CacheOrDumpToFile(sb, outPath, true);
        }

        DumpNamespace(sb, outPath);
    }

    private void HandleNamespace(List<CSType> csTypes)
    {
        Dictionary<string, CSClassType> dicClass = new();
        foreach (var csType in csTypes)
        {
            if (csType is CSClassType csClassType)
            {
                if (csClassType.Namespace.Length > 0)
                {
                    var firstNamespace = csClassType.Namespace.Split('.').FirstOrDefault();
                    if (firstNamespace != null)
                    {
                        NamespaceDict.TryAdd(firstNamespace, true);
                    }
                }
                else
                {
                    NamespaceDict.TryAdd(csClassType.Name, false);
                }
                var fullClassName = csClassType.Namespace.Length > 0 ? csClassType.Namespace + "." + csClassType.Name : csClassType.Name;
                dicClass[fullClassName] = csClassType;
            }
        }
        foreach (var csType in csTypes)
        {
            if (!csType.IsNamespace && csType.Namespace.Length > 0)
            {

                if (dicClass.TryGetValue(csType.Namespace, out var parent))
                {
                    parent.Fields.Add(new CSTypeField()
                    {
                        Name = csType.Name,
                        TypeName = csType.Namespace + "." + csType.Name,
                        Location = csType.Location,
                        Comment = csType.Comment,
                    });
                }
            }
        }
    }

    private void DumpNamespace(StringBuilder sb, string outPath)
    {
        sb.AppendLine("CS = {}");
        foreach (var (namespaceString, isNamespace) in NamespaceDict)
        {
            if (isNamespace)
            {
                sb.AppendLine($"---@type namespace <\"{namespaceString}\">\nCS.{namespaceString} = {{}}");
            }
            else
            {
                sb.AppendLine($"---@type {namespaceString}\nCS.{namespaceString} = {{}}");
            }
        }

        var filePath = Path.Combine(outPath, "xlua_namespace.lua");
        File.WriteAllText(filePath, sb.ToString());
    }

    private void CacheOrDumpToFile(StringBuilder sb, string outPath, bool force = false)
    {
        if (sb.Length > SingleFileLength || force)
        {
            var filePath = Path.Combine(outPath, $"xlua_dump_{Count}.lua");
            File.WriteAllText(filePath, sb.ToString());
            ResetSb(sb);
            Count++;
        }
    }

    private void ResetSb(StringBuilder sb)
    {
        sb.Clear();
        sb.AppendLine("---@meta");
    }

    private void HandleCsClassType(CSClassType csClassType, StringBuilder sb)
    {
        

        var classFullName = csClassType.Name;
        if (csClassType.Namespace.Length > 0)
        {
            classFullName = $"{csClassType.Namespace}.{csClassType.Name}";
        }

        WriteCommentAndLocation(csClassType.Comment, csClassType.Location, sb);
        WriteTypeAnnotation("class", classFullName, csClassType.BaseClass, csClassType.Interfaces,
            csClassType.GenericTypes, sb);
        if (!csClassType.IsStatic)
        {
            var ctors = GetCtorList(csClassType);
            if (ctors.Count > 0)
            {
                foreach (var ctor in ctors)
                {
                    var paramsString = string.Join(",",
                        ctor.Params.Select(it => $"{it.Name}: {Util.CovertToLuaTypeName(it.TypeName)}"));
                    sb.AppendLine(
                        $"---@overload fun({paramsString}): {classFullName}");
                }
            }
            else
            {
                sb.AppendLine($"---@overload fun(): {classFullName}");
            }
        }

        sb.AppendLine($"local {csClassType.Name} = {{}}");
        foreach (var csTypeField in csClassType.Fields)
        {
            if (csTypeField.Name == "this[]" || Util.IsLuaKeywords(csTypeField.Name))
            {
                continue;
            }
            WriteCommentAndLocation(csTypeField.Comment, csTypeField.Location, sb);
            sb.AppendLine($"---@type {Util.CovertToLuaTypeName(csTypeField.TypeName)}");
            sb.AppendLine($"{csClassType.Name}.{csTypeField.Name} = nil");
            sb.AppendLine();
        }

        List<CSTypeMethod> methods = new();
        Dictionary<string, CSTypeMethod> eventMethods = new();

        foreach (var method in csClassType.Methods)
        {
            if (method.Name == ".ctor" || Util.IsLuaKeywords(method.Name))
            {
                continue;
            }
            if (method.Kind == MethodKind.EventAdd || method.Kind == MethodKind.EventRemove)
            {
                var name = method.Name.Replace("add_", "").Replace("remove_", "");
                string op = method.Kind == MethodKind.EventAdd ? "\"+\"" : "\"-\"";
                if (!eventMethods.ContainsKey(name))
                {
                    methods.Add(method);
                    eventMethods.Add(name, method);
                    method.Name = name;
                    method.Params.Insert(0, new CSParam()
                    {
                        Name = "op",
                        Nullable = false,
                        TypeName = op
                    });
                }else
                {
                    var m = eventMethods[name];
                    m.Params[0].TypeName += "|" + op;
                }
            }
            else
            {
                methods.Add(method);
            }
        }

        foreach (var csTypeMethod in methods)
        {

            WriteCommentAndLocation(csTypeMethod.Comment, csTypeMethod.Location, sb);

            var outParams = new List<CSParam>();
            foreach (var param in csTypeMethod.Params)
            {
                if (param.Kind is RefKind.Out or RefKind.Ref)
                {
                    outParams.Add(param);
                }

                if (param.Kind != RefKind.Out)
                {
                    var name = Util.CovertToLuaCompactName(param.IsParams ? "..." : param.Name);
                    var typeName = Util.CovertToLuaTypeName(param.IsParams ? param.TypeName.Replace("[]","") : param.TypeName);
                    var comment = param.Comment;
                    if (param.Nullable)
                    {
                        typeName += "?";
                    }
                    if (comment.Length > 0)
                    {
                        comment = comment.Replace("\n", "\n---");
                        sb.AppendLine($"---@param {name} {typeName} {comment}");
                    }
                    else
                    {
                        sb.AppendLine($"---@param {name} {typeName}");
                    }
                }
            }
            if (outParams.Count == 0)
            {
                sb.Append($"---@return {Util.CovertToLuaTypeName(csTypeMethod.ReturnTypeName)}");
            }
            else
            {
                if (csTypeMethod.ReturnTypeName == "void")
                {
                    sb.Append($"---@return ");
                }
                else
                {
                    sb.Append($"---@return {Util.CovertToLuaTypeName(csTypeMethod.ReturnTypeName)}, ");
                }
                for (var i = 0; i < outParams.Count; i++)
                {
                    sb.Append($"{Util.CovertToLuaTypeName(outParams[i].TypeName)}");
                    if (i < outParams.Count - 1)
                    {
                        sb.Append(", ");
                    }
                }
            }

            sb.AppendLine();

            var dot = csTypeMethod.IsStatic ? "." : ":";
            sb.Append($"function {csClassType.Name}{dot}{csTypeMethod.Name}(");
            for (var i = 0; i < csTypeMethod.Params.Count; i++)
            {
                if (csTypeMethod.Params[i].IsParams)
                {
                    sb.Append("...");   
                }else
                {
                    sb.Append(Util.CovertToLuaCompactName(csTypeMethod.Params[i].Name));
                }
                if (i < csTypeMethod.Params.Count - 1)
                {
                    sb.Append(", ");
                }
            }

            sb.AppendLine(")");
            sb.AppendLine("end");
            sb.AppendLine();
        }
    }

    private void WriteTypeAnnotation(string tag, string fullName, string baseClass, List<string> interfaces,
        List<string> genericTypes,
        StringBuilder sb)
    {
        sb.Append($"---@{tag} {fullName}");
        if (genericTypes.Count > 0)
        {
            sb.Append($"<{genericTypes[0]}>");
            for (var i = 1; i < genericTypes.Count; i++)
            {
                sb.Append($", {genericTypes[i]}");
            }

            sb.Append('>');
        }

        if (!string.IsNullOrEmpty(baseClass))
        {
            sb.Append($": {baseClass}");
            foreach (var csInterface in interfaces)
            {
                sb.Append($", {csInterface}");
            }
        }
        else if (interfaces.Count > 0)
        {
            sb.Append($": {interfaces[0]}");
            for (var i = 1; i < interfaces.Count; i++)
            {
                sb.Append($", {interfaces[i]}");
            }
        }

        sb.Append('\n');
    }

    private void WriteCommentAndLocation(string comment, string location, StringBuilder sb)
    {
        if (comment.Length > 0)
        {
            sb.AppendLine($"---{comment.Replace("\n", "\n---")}");
        }

        if (location.StartsWith("file://"))
        {
            location = location.Replace("\"", "'");
            sb.AppendLine($"---@source \"{location}\"");
        }
    }

    private void HandleCsInterface(CSInterface csInterface, StringBuilder sb)
    {
        sb.AppendLine($"---@interface {csInterface.Name}");
    }

    private void HandleCsEnumType(CSEnumType csEnumType, StringBuilder sb)
    {
        if (csEnumType.Namespace.Length > 0)
        {
            var firstNamespace = csEnumType.Namespace.Split('.').FirstOrDefault();
            if (firstNamespace != null)
            {
                NamespaceDict.TryAdd(firstNamespace, true);
            }
        }
        else
        {
            NamespaceDict.TryAdd(csEnumType.Name, false);
        }

        var classFullName = csEnumType.Name;
        if (csEnumType.Namespace.Length > 0)
        {
            classFullName = $"{csEnumType.Namespace}.{csEnumType.Name}";
        }

        WriteCommentAndLocation(csEnumType.Comment, csEnumType.Location, sb);
        WriteTypeAnnotation("enum", $"{classFullName}", string.Empty, [], [], sb);
        
        sb.AppendLine($"local {csEnumType.Name} = {{}}");
        foreach (var csTypeField in csEnumType.Fields)
        {
            WriteCommentAndLocation(csTypeField.Comment, csTypeField.Location, sb);
            sb.AppendLine("---@type integer");
            sb.AppendLine($"{csEnumType.Name}.{csTypeField.Name} = nil");
            sb.AppendLine();
        }
        
        //sb.AppendLine($"---@enum (key) {classFullName}");
        //foreach (var csTypeField in csEnumType.Fields)
        //{
        //    sb.AppendLine($"---| CS.{csEnumType.Name}.{csTypeField.Name}");
        //}
    }

    private void HandleCsDelegate(CSDelegate csDelegate, StringBuilder sb)
    {
        var paramsString = string.Join(",",
            csDelegate.InvokeMethod.Params.Select(it => $"{it.Name}: {Util.CovertToLuaTypeName(it.TypeName)}"));
        var classFullName = csDelegate.Name;
        var outParams = csDelegate.InvokeMethod.Params.Where(it => it.Kind is RefKind.Out or RefKind.Ref).ToList();

        if (csDelegate.Namespace.Length > 0)
        {
            classFullName = $"{csDelegate.Namespace}.{csDelegate.Name}";
        }
        List<string> ret = outParams.Select(it => $"{Util.CovertToLuaTypeName(it.TypeName)}").ToList();
        if (ret.Count == 0 || csDelegate.InvokeMethod.ReturnTypeName != "void")
        {
            ret.Insert(0, Util.CovertToLuaTypeName(csDelegate.InvokeMethod.ReturnTypeName));
        }
        
        sb.AppendLine($"---@alias {classFullName}Ty fun({paramsString}): {string.Join(",", ret)}");
        sb.AppendLine();
        sb.AppendLine($"---@interface {classFullName}");
        sb.AppendLine($"---@overload fun(func: fun({paramsString}): {string.Join(",", ret)}):any");
    }

    private List<CSTypeMethod> GetCtorList(CSClassType csClassType)
    {
        return csClassType.Methods.FindAll(method => method.Name == ".ctor");
    }
}