using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace RockEngine.ShaderSyntax
{
    internal static class GlslContentTypeDefinitions
    {
        public const string GlslContentType = "glsl";

        [Export]
        [Name(GlslContentType)]
        [BaseDefinition("code")]
        internal static ContentTypeDefinition GlslContentTypeDefinition = new ContentTypeDefinition();

        [Export]
        [FileExtension(".vert")]
        [ContentType(GlslContentType)]
        internal static FileExtensionToContentTypeDefinition VertFileExtension = new FileExtensionToContentTypeDefinition();

        [Export]
        [FileExtension(".frag")]
        [ContentType(GlslContentType)]
        internal static FileExtensionToContentTypeDefinition FragFileExtension = new FileExtensionToContentTypeDefinition();

        [Export]
        [FileExtension(".glsl")]
        [ContentType(GlslContentType)]
        internal static FileExtensionToContentTypeDefinition GlslFileExtension = new FileExtensionToContentTypeDefinition();

        [Export]
        [FileExtension(".comp")]
        [ContentType(GlslContentType)]
        internal static FileExtensionToContentTypeDefinition CompFileExtension = new FileExtensionToContentTypeDefinition();
    }

    internal static class GlslClassificationTypes
    {
        public const string GlslKeyword = "glsl_keyword";
        public const string GlslType = "glsl_type";
        public const string GlslPreprocessor = "glsl_preprocessor";
        public const string GlslMaterialAnnotation = "glsl_material_annotation";
        public const string GlslBuiltInVariable = "glsl_builtin_variable";
        public const string GlslBuiltInFunction = "glsl_builtin_function";
        public const string GlslUserFunction = "glsl_user_function";

        [Export]
        [Name(GlslKeyword)]
        [BaseDefinition("keyword")]
        internal static ClassificationTypeDefinition GlslKeywordDefinition = new ClassificationTypeDefinition();

        [Export]
        [Name(GlslType)]
        [BaseDefinition("type")]
        internal static ClassificationTypeDefinition GlslTypeDefinition = new ClassificationTypeDefinition();

        [Export]
        [Name(GlslPreprocessor)]
        [BaseDefinition("preprocessor keyword")]
        internal static ClassificationTypeDefinition GlslPreprocessorDefinition = new ClassificationTypeDefinition();

        [Export]
        [Name(GlslMaterialAnnotation)]
        [BaseDefinition("string")]
        internal static ClassificationTypeDefinition GlslMaterialAnnotationDefinition = new ClassificationTypeDefinition();

        [Export]
        [Name(GlslBuiltInVariable)]
        [BaseDefinition("identifier")] // or "keyword" – choose a base style
        internal static ClassificationTypeDefinition GlslBuiltInVariableDefinition = new ClassificationTypeDefinition();


        [Export]
        [Name(GlslBuiltInFunction)]
        [BaseDefinition("identifier")]
        internal static ClassificationTypeDefinition GlslBuiltInFunctionDefinition = new ClassificationTypeDefinition();

        [Export]
        [Name(GlslUserFunction)]
        [BaseDefinition("identifier")]
        internal static ClassificationTypeDefinition GlslUserFunctionDefinition = new ClassificationTypeDefinition();
    }
}
