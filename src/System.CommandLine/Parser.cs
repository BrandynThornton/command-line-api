#define UNNAMED
// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace System.CommandLine
{
    public class Parser
    {
        public Parser(CommandLineConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public Parser(params SymbolDefinition[] symbolDefinitions) : this(new CommandLineConfiguration(symbolDefinitions))
        {
        }

        internal CommandLineConfiguration Configuration { get; }

        public virtual ParseResult Parse(IReadOnlyCollection<string> arguments, string rawInput = null)
        {
            var rawTokens = arguments;  // allow a more user-friendly name for callers of Parse
            var lexResult = NormalizeRootCommand(rawTokens).Lex(Configuration);
            var unparsedTokens = new Queue<Token>(lexResult.Tokens);
            var allSymbols = new List<Symbol>();
            var errors = new List<ParseError>(lexResult.Errors);
            var unmatchedTokens = new List<string>();
            Command rootCommand = null;
            Command innermostCommand = null;

            /*
             * Each option needs to know which command it belongs to
             * Once a new command is encountered, missing previous options should be discarded or defaulted
             * Then options toward the next command can be assumed as needed
             */


#if UNNAMED
            var optionQueue = GatherOptions(Configuration.SymbolDefinitions).ToList();
#endif

            while (unparsedTokens.Any())
            {
                var token = unparsedTokens.Dequeue();

                if (token.Type == TokenType.EndOfArguments)
                {
                    // stop parsing further tokens
                    break;
                }

                if (token.Type != TokenType.Argument)
                {
                    var symbolDefinition =
                        Configuration.SymbolDefinitions
                                     .SingleOrDefault(o => o.HasAlias(token.Value));

                    if (symbolDefinition != null)
                    {
                        var symbol = allSymbols
                            .LastOrDefault(o => o.HasAlias(token.Value));

                        if (symbol == null)
                        {
                            symbol = Symbol.Create(symbolDefinition, token.Value, validationMessages: Configuration.ValidationMessages);

                            //rootCommands.Add(token.Value, (Command)symbol);
                            rootCommand = (Command)symbol;
                        }

                        allSymbols.Add(symbol);

                        continue;
                    }
                }

                var added = false;

                foreach (var topLevelSymbol in Enumerable.Reverse(allSymbols))
                {
                    Symbol symbolForToken = topLevelSymbol.TryTakeToken(token);

                    if (symbolForToken != null)
                    {
                        allSymbols.Add(symbolForToken);
                        if (symbolForToken is Command command)
                        {
                            innermostCommand = command;
                        }

                        added = true;
#if UNNAMED
                        if (token.Type is TokenType.Option)
                        {
                            var existing = optionQueue.FirstOrDefault(symdef => symdef.Name == symbolForToken.Name);
                            if (existing != null)
                            {
                                // we've used this option - don't use it again
                                optionQueue.Remove(existing);
                            }
                        }
#endif
                        break;
                    }

                    if (token.Type == TokenType.Argument &&
                        topLevelSymbol.SymbolDefinition is CommandDefinition)
                    {
#if UNNAMED
                        var optionSymdef = optionQueue.FirstOrDefault();
                        if (optionSymdef != null)
                        {
                            //if (!optionSymdef.ArgumentDefinition.HasDefaultValue ||
                            //    optionSymdef.ArgumentDefinition.GetDefaultValue().ToString() != token.Value)
                            {
                                optionQueue.RemoveAt(0);
                                var newToken = new Token("-" + optionSymdef.Name, TokenType.Option);
                                symbolForToken = topLevelSymbol.TryTakeToken(newToken);
                                Symbol nextSymbolForToken = symbolForToken?.TryTakeToken(token);
                                if (nextSymbolForToken != null)
                                {
                                    allSymbols.Add(symbolForToken);
                                    //allSymbols.Add(nextSymbolForToken);
                                    added = true;
                                }
                            }
                        }
#endif
                        break;
                    }
                }

                if (!added)
                {
                    unmatchedTokens.Add(token.Value);
                }
            }

            if (Configuration.RootCommandDefinition.TreatUnmatchedTokensAsErrors)
            {
                errors.AddRange(
                    unmatchedTokens.Select(token => new ParseError(Configuration.ValidationMessages.UnrecognizedCommandOrArgument(token))));
            }

            return new ParseResult(
                rootCommand,
                innermostCommand ?? rootCommand,
                rawTokens,
                unparsedTokens.Select(t => t.Value).ToArray(),
                unmatchedTokens,
                errors,
                rawInput);
        }

#if UNNAMED
        private IEnumerable<SymbolDefinition> GatherOptions(SymbolDefinitionSet symbolDefinitions)
        {
            var optionList = new List<SymbolDefinition>();
            foreach (var symDef in symbolDefinitions) //.Where( s => s is OptionDefinition))
            {
                if (symDef is OptionDefinition)
                {
                    var validator = symDef.ArgumentDefinition.Parser.ArityValidator;
                    if (validator?.MaximumNumberOfArguments == 1 &&
                        validator.MinimumNumberOfArguments == 1)    // Exactly One
                    {
                        optionList.Add(symDef);
                    }
                }

                optionList.AddRange(GatherOptions(symDef.SymbolDefinitions));
            }
            return optionList;
        }
#endif

        internal IReadOnlyCollection<string> NormalizeRootCommand(IReadOnlyCollection<string> args)
        {
            var firstArg = args.FirstOrDefault();

            var commandName = Configuration.RootCommandDefinition.Name;

            if (string.Equals(firstArg, commandName, StringComparison.OrdinalIgnoreCase))
            {
                return args;
            }

            if (firstArg != null &&
                firstArg.Contains(Path.DirectorySeparatorChar) &&
                (firstArg.EndsWith(commandName, StringComparison.OrdinalIgnoreCase) ||
                 firstArg.EndsWith($"{commandName}.exe", StringComparison.OrdinalIgnoreCase)))
            {
                args = new[] { commandName }.Concat(args.Skip(1)).ToArray();
            }
            else
            {
                args = new[] { commandName }.Concat(args).ToArray();
            }

            return args;
        }
    }
}
