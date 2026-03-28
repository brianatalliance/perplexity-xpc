# Resources

Place the following file here before building:

- **perplexity.ico** — Application icon used in the Explorer context menu.
  The icon should be a 256×256 (with 32×32, 16×16 fallback frames) `.ico` file.

The `.csproj` will embed this as a resource and reference it as the `ApplicationIcon`.

If the icon is missing, the build will still succeed — the context menu entries will
simply use the default application icon.
