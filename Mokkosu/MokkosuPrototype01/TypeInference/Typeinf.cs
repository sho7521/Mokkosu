﻿using Mokkosu.AST;
using Mokkosu.Utils;
using Mokkosu.Parsing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mokkosu.TypeInference
{
    using TEnv = MEnv<MTypeScheme>;

    static class Typeinf
    {
        /// <summary>
        /// 型推論を開始する
        /// </summary>
        /// <param name="parse_result">構文解析の結果</param>
        public static void Start(ParseResult parse_result)
        {
            var ctx = new TypeInfContext();
            ctx.TEnv = InitialEnv();
            parse_result.TopExprs.ForEach(e => TypeinfTopExpr(e, ctx));
        }

        static TEnv InitialEnv()
        {
            var int_int_int = new FunType(new IntType(), new FunType(new IntType(), new IntType()));

            var dict = new Dictionary<string, MTypeScheme>()
            {
                { "__operator_pls", new MTypeScheme(int_int_int) },
                { "__operator_mns", new MTypeScheme(int_int_int) },
                { "__operator_ast", new MTypeScheme(int_int_int) },
                { "__operator_sls", new MTypeScheme(int_int_int) },
            };

            var tenv = new TEnv();
            foreach (var kv in dict)
            {
                tenv = tenv.Cons(kv.Key, kv.Value);
            }
            return tenv; 
        }


        /// <summary>
        /// トップレベル式の型検査＆型推論
        /// </summary>
        /// <param name="top_expr">トップレベル式</param>
        /// <param name="ctx">型推論文脈</param>
        static void TypeinfTopExpr(MTopExpr top_expr, TypeInfContext ctx)
        {
            if (top_expr is MUserTypeDef)
            {
                TypeinfUserTypeDef((MUserTypeDef)top_expr, ctx);
                System.Console.WriteLine(ctx);
            }
            else if (top_expr is MTopDo)
            {
                TypeinfTopDo((MTopDo)top_expr, ctx);
                var e = (MTopDo)top_expr;
                System.Console.WriteLine("{0} : {1}", e.Expr, e.Type);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// ユーザ型定義の型検査
        /// </summary>
        /// <param name="typedef">ユーザ型定義</param>
        /// <param name="ctx">型推論文脈</param>
        static void TypeinfUserTypeDef(MUserTypeDef typedef, TypeInfContext ctx)
        {
            var user_types = new MEnv<int>();
            foreach (var def in typedef.Items)
            {
                var name = def.Name;
                var kind = def.TypeParams.Count;
                user_types = user_types.Cons(name, kind);
            }
            ctx.UserTypes = ctx.UserTypes.Append(user_types);

            var tag_env = new MEnv<Tag>();
            foreach (var def in typedef.Items)
            {
                tag_env = tag_env.Append(TypeinfTypeDefItem(def.Tags, def.Name, def.TypeParams, ctx));
            }
            ctx.TagEnv = ctx.TagEnv.Append(tag_env);
        }

        /// <summary>
        /// ユーザ定義型のアイテムごとの型検査
        /// </summary>
        /// <param name="tags">タグの列</param>
        /// <param name="type_name">型名</param>
        /// <param name="type_params">型パラメータ</param>
        /// <param name="ctx">型推論文脈</param>
        /// <returns>タグ環境</returns>
        static MEnv<Tag> TypeinfTypeDefItem(List<TagDef> tags, string type_name, 
            List<string> type_params, TypeInfContext ctx)
        {
            var tag_env = new MEnv<Tag>();

            for (var i = 0; i < tags.Count; i++)
            {
                var name = tags[i].Name;
                var index = i;

                var dict = new Dictionary<string, TypeVar>();
                var bounded = new MSet<int>();
                var type_args = new List<MType>();
                foreach (var p in type_params)
                {
                    var tv = new TypeVar();
                    dict.Add(p, tv);
                    bounded = bounded.Union(new MSet<int>(tv.Id));
                    type_args.Add(tv);
                }

                var arg_types = tags[i].Args.Select(typ => MapTypeParam(typ, dict));
                var type = new UserType(type_name, type_args);

                var tag = new Tag(name, index, bounded, arg_types.ToList(), type);
                tag_env = tag_env.Cons(name, tag);
            }

            return tag_env;
        }

        /// <summary>
        /// 型中の型パラメータを表すUserTypeを型変数に置換する
        /// </summary>
        /// <param name="type">型</param>
        /// <param name="dict">型パラメータと型変数の対応</param>
        /// <returns>型</returns>
        static MType MapTypeParam(MType type, Dictionary<string, TypeVar> dict)
        {
            if (type is TypeVar)
            {
                var t = (TypeVar)type;
                if (t.Value == null)
                {
                    return type;
                }
                else
                {
                    return MapTypeParam(t.Value, dict);
                }
            }
            else if (type is UserType)
            {
                var t = (UserType)type;
                if (t.Args.Count == 0 && dict.ContainsKey(t.Name))
                {
                    return dict[t.Name];
                }
                else
                {
                    var args = new List<MType>();
                    foreach (var arg in t.Args)
                    {
                        args.Add(MapTypeParam(arg, dict));
                    }
                    return new UserType(t.Name, args);
                }
            }
            else if (type is IntType || type is DoubleType || type is StringType ||
                type is CharType || type is UnitType || type is BoolType)
            {
                return type;
            }
            else if (type is FunType)
            {
                var t = (FunType)type;
                var arg = MapTypeParam(t.ArgType, dict);
                var ret = MapTypeParam(t.RetType, dict);
                return new FunType(arg, ret);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// トップレベルdoの型推論
        /// </summary>
        /// <param name="top_do"></param>
        /// <param name="ctx"></param>
        static void TypeinfTopDo(MTopDo top_do, TypeInfContext ctx)
        {
            Inference(top_do.Expr, top_do.Type, ctx.TEnv, ctx);
        }

        /// <summary>
        /// 型推論 (Algorithm M)
        /// </summary>
        /// <param name="expr">型を推論する式</param>
        /// <param name="type">文脈の型</param>
        /// <param name="ctx">型推論文脈</param>
        static void Inference(MExpr expr, MType type, TEnv tenv, TypeInfContext ctx)
        {
            if (expr is MInt)
            {
                Unification(type, new IntType());
            }
            else if (expr is MDouble)
            {
                Unification(type, new DoubleType());
            }
            else if (expr is MString)
            {
                Unification(type, new StringType());
            }
            else if (expr is MChar)
            {
                Unification(type, new CharType());
            }
            else if (expr is MUnit)
            {
                Unification(type, new UnitType());
            }
            else if (expr is MBool)
            {
                Unification(type, new BoolType());
            }
            else if (expr is MTag)
            {
                var e = (MTag)expr;
                Tag tag;
                if (ctx.TagEnv.Lookup(e.Name, out tag))
                {
                    tag = GeneralizeTag(tag);
                    e.Index = tag.Index;
                    if (e.Args.Count == tag.ArgTypes.Count)
                    {
                        for (int i = 0; i < e.Args.Count; i++)
                        {
                            Inference(e.Args[i], tag.ArgTypes[i], tenv, ctx);
                        }
                        Unification(type, tag.Type);
                    }
                    else
                    {
                        throw new MError("型エラー");
                    }
                }
                else
                {
                    throw new MError("タグ" + e.Name + "は定義されていません");
                }
            }
            else if (expr is MVar)
            {
                var e = (MVar)expr;
                MTypeScheme typescheme;
                if (tenv.Lookup(e.Name, out typescheme))
                {
                    var t = Instantiate(typescheme);
                    Unification(e.Type, t);
                    Unification(type, t);
                }
                else
                {
                    throw new MError(string.Format("変数{0}は未定義です", e.Name));
                }
            }
            else if (expr is MLambda)
            {
                var e = (MLambda)expr;
                var tenv2 = tenv.Cons(e.ArgName, new MTypeScheme(e.ArgType));
                var ret_type = new TypeVar();
                Inference(e.Body, ret_type, tenv2, ctx);
                var fun_type = new FunType(e.ArgType, ret_type);
                Unification(type, fun_type);
            }
            else if (expr is MApp)
            {
                var e = (MApp)expr;
                var arg_type = new TypeVar();
                var fun_type = new FunType(arg_type, type);
                Inference(e.FunExpr, fun_type, tenv, ctx);
                Inference(e.ArgExpr, arg_type, tenv, ctx);
            }
            else if (expr is MIf)
            {
                var e = (MIf)expr;
                Inference(e.CondExpr, new BoolType(), tenv, ctx);
                Inference(e.ThenExpr, type, tenv, ctx);
                Inference(e.ElseExpr, type, tenv, ctx);
            }
            else if (expr is MMatch)
            {
                var e = (MMatch)expr;
                var t = new TypeVar();
                var tenv2 = InferencePat(e.Pat, t, tenv, ctx);
                Inference(e.Expr, t, tenv, ctx);
                var tenv3 = tenv2.Append(tenv);
                Inference(e.ThenExpr, type, tenv3, ctx);
                Inference(e.ElseExpr, type, tenv, ctx);
            }
            else
            {
                throw new NotImplementedException();
            }
        }


        static TEnv InferencePat(MPat pat, MType type, TEnv tenv, TypeInfContext ctx)
        {
            if (pat is PWild)
            {
                var p = (PWild)pat;
                Unification(type, p.Type);
                return new TEnv();
            }
            else if (pat is PVar)
            {
                var p = (PVar)pat;
                Unification(type, p.Type);
                return new TEnv().Cons(p.Name, new MTypeScheme(type));
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// 出現検査
        /// </summary>
        /// <param name="id">型変数ID</param>
        /// <param name="type">型</param>
        /// <returns>型に型変数IDが含まれていれば真そうでなければ偽</returns>
        static bool OccursCheck(int id, MType type)
        {
            if (type is TypeVar)
            {
                var t = (TypeVar)type;
                if (t.Id == id)
                {
                    return true;
                }
                else if (t.Value == null)
                {
                    return false;
                }
                else
                {
                    return OccursCheck(id, t.Value);
                }
            }
            else if (type is FunType)
            {
                var t = (FunType)type;
                return OccursCheck(id, t.ArgType) || OccursCheck(id, t.RetType);
            }
            else if (type is IntType || type is DoubleType || type is StringType ||
                type is CharType || type is UnitType || type is BoolType)
            {
                return false;
            }
            else if (type is UserType)
            {
                var t = (UserType)type;
                bool b = false;
                foreach (var arg in t.Args)
                {
                    if (OccursCheck(id, arg))
                    {
                        b = true;
                        break;
                    }
                }
                return b;
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// 単一化
        /// </summary>
        /// <param name="type1">型1</param>
        /// <param name="type2">型2</param>
        static void Unification(MType type1, MType type2)
        {
            if (type1 is TypeVar)
            {
                var t1 = (TypeVar)type1;
                if (type2 is TypeVar && t1.Id == ((TypeVar)type2).Id)
                {
                    return;
                }
                else if (t1.Value == null)
                {
                    if (OccursCheck(t1.Id, type2))
                    {
                        throw new MError("型エラー (出現違反)");
                    }
                    else
                    {
                        t1.Value = type2;
                    }
                }
                else
                {
                    Unification(t1.Value, type2);
                }
            }
            else if (type2 is TypeVar)
            {
                var t2 = (TypeVar)type2;
                if (t2.Value == null)
                {
                    if (OccursCheck(t2.Id, type1))
                    {
                        throw new MError("型エラー (出現違反)");
                    }
                    else
                    {
                        t2.Value = type1;
                    }
                }
                else
                {
                    Unification(t2.Value, type1);
                }
            }
            else if (type1 is UserType && type2 is UserType)
            {
                var t1 = (UserType)type1;
                var t2 = (UserType)type2;
                if (t1.Name == t2.Name && t1.Args.Count == t2.Args.Count)
                {
                    for (int i = 0; i < t1.Args.Count; i++)
                    {
                        Unification(t1.Args[i], t2.Args[i]);
                    }
                }
                else
                {
                    throw new MError("型エラー (単一化エラー)");
                }
            }
            else if (type1 is FunType && type2 is FunType)
            {
                var t1 = (FunType)type1;
                var t2 = (FunType)type2;
                Unification(t1.ArgType, t2.ArgType);
                Unification(t1.RetType, t2.RetType);
            }
            else if (type1 is IntType && type2 is IntType)
            {
                return;
            }
            else if (type1 is DoubleType && type2 is DoubleType)
            {
                return;
            }
            else if (type1 is StringType && type2 is StringType)
            {
                return;
            }
            else if (type1 is CharType && type2 is CharType)
            {
                return;
            }
            else if (type1 is UnitType && type2 is UnitType)
            {
                return;
            }
            else if (type1 is BoolType && type2 is BoolType)
            {
                return;
            }
            else
            {
                throw new MError("型エラー (単一化エラー)");
            }
        }

        /// <summary>
        /// 型中に自由に出現する型変数の集合を返す
        /// </summary>
        /// <param name="type">型</param>
        /// <returns>自由に出現する型変数の集合</returns>
        static MSet<int> FreeTypeVars(MType type)
        {
            if (type is TypeVar)
            {
                var t = (TypeVar)type;
                if (t.Value == null)
                {
                    return new MSet<int>();
                }
                else
                {
                    return FreeTypeVars(t.Value);
                }
            }
            else if (type is UserType)
            {
                var t = (UserType)type;
                var set = new MSet<int>();
                foreach (var arg in t.Args)
                {
                    set = set.Union(FreeTypeVars(arg));
                }
                return set;
            }
            else if (type is IntType || type is DoubleType || type is StringType ||
                type is CharType || type is UnitType || type is BoolType)
            {
                return new MSet<int>();
            }
            else if (type is FunType)
            {
                var t = (FunType)type;
                var set1 = FreeTypeVars(t.ArgType);
                var set2 = FreeTypeVars(t.RetType);
                return set1.Union(set2);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// 型スキーム中で自由に出現する型変数の集合を返す
        /// </summary>
        /// <param name="typescheme">型スキーム</param>
        /// <returns>自由に出現する型変数の集合</returns>
        static MSet<int> FreeTypeVars(MTypeScheme typescheme)
        {
            var set = FreeTypeVars(typescheme.Type);
            return set.Diff(typescheme.Bounded);
        }

        /// <summary>
        /// 型環境中で自由に出現する型変数の集合を返す
        /// </summary>
        /// <param name="tenv">型環境</param>
        /// <returns>自由に出現する型変数の集合</returns>
        static MSet<int> FreeTypeVars(TEnv tenv)
        {
            if (tenv.IsEmpty())
            {
                return new MSet<int>();
            }
            else
            {
                var set1 = FreeTypeVars(tenv.Head.Item2);
                var set2 = FreeTypeVars(tenv.Tail);
                return set1.Union(set2);
            }
        }

        /// <summary>
        /// 型に量化子(∀)を付ける
        /// </summary>
        /// <param name="tenv"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        static MTypeScheme Generalize(TEnv tenv, MType type)
        {
            var tenv_fvs = FreeTypeVars(tenv);
            var fvs = FreeTypeVars(type);
            var bounded = fvs.Diff(tenv_fvs);
            return new MTypeScheme(bounded.ToArray(), type);
        }

        /// <summary>
        /// 型スキームからインスタンスを作成
        /// </summary>
        /// <param name="typescheme">型スキーム</param>
        /// <returns>新しい型</returns>
        static MType Instantiate(MTypeScheme typescheme)
        {
            var map = new Dictionary<int, MType>();
            foreach (var id in typescheme.Bounded.ToArray())
            {
                map.Add(id, new TypeVar());
            }
            return MapTypeVar(map, typescheme.Type);
        }

        /// <summary>
        /// タグのインスタンスを作成
        /// </summary>
        /// <param name="tag">もととなるタグ</param>
        /// <returns>生成されたタグ</returns>
        static Tag GeneralizeTag(Tag tag)
        {
            var map = new Dictionary<int, MType>();
            foreach (var id in tag.Bounded.ToArray())
            {
                map.Add(id, new TypeVar());
            }
            var arg_types = tag.ArgTypes.Select(t => MapTypeVar(map, t));
            var type = MapTypeVar(map, tag.Type);
            return new Tag(tag.Name, tag.Index, tag.Bounded, arg_types.ToList(), type);
        }

        /// <summary>
        /// 型変数を辞書にしたがって新しいものに置き換える
        /// </summary>
        /// <param name="map">辞書</param>
        /// <param name="type">型</param>
        /// <returns>置換後の型</returns>
        static MType MapTypeVar(Dictionary<int, MType> map, MType type)
        {
            if (type is TypeVar)
            {
                var t = (TypeVar)type;
                if (t.Value == null)
                {
                    if (map.ContainsKey(t.Id))
                    {
                        return map[t.Id];
                    }
                    else
                    {
                        return type;
                    }
                }
                else
                {
                    return MapTypeVar(map, t.Value);
                }
            }
            else if (type is UserType)
            {
                var t = (UserType)type;
                var args = new List<MType>();
                foreach (var arg in t.Args)
                {
                    args.Add(MapTypeVar(map, arg));
                }
                return new UserType(t.Name, args);
            }
            else if (type is IntType || type is DoubleType || type is StringType ||
                type is CharType || type is UnitType || type is BoolType)
            {
                return type;
            }
            else if (type is FunType)
            {
                var t = (FunType)type;
                var arg = MapTypeVar(map, t.ArgType);
                var ret = MapTypeVar(map, t.RetType);
                return new FunType(arg, ret);
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }
}
