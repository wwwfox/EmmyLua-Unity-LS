namespace EmmyLua.Unity.Generator;

public static class Util
{
    public static string CovertToLuaCompactName(string name)
    {
        return name switch
        {
            "function" or "end" or "local" or "nil" or "true" or "false" or "and" or "or" or "not" or "if" or "then"
                or "else" or "elseif" or "while" or "do" or "repeat" or "until" or "for" or "in" or "break" or "return"
                or "goto" => "_" + name,
            _ => name
        };
    }
    
    public static bool IsLuaKeywords(string name)
    {
        return name switch
        {
            "function" or "end" or "local" or "nil" or "true" or "false" or "and" or "or" or "not" or "if" or "then"
                or "else" or "elseif" or "while" or "do" or "repeat" or "until" or "for" or "in" or "break" or "return"
                or "goto" => true,
            _ => false
        };
    }
    
    public static string CovertToLuaTypeName(string name)
    {
        return name switch
        {
            "int" => "integer",
            "long" => "integer",
            "ulong" => "integer",
            "float" => "number",
            "double" => "number",
            "sbyte" => "number",
            "byte" => "number",
            "short" => "number",
            "ushort" => "number",
            "uint" => "number",
            "char" => "number",
            "bool" => "boolean",
            "string" => "string",
            "byte[]" => "string",
            "object" => "table",
            "decimal" => "userdata",
            _ => name
        };
    }
}