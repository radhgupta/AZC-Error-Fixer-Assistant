import "@typespec/http";

using TypeSpec.Http;

@service(#{title: "Sample API"})
namespace Azure.ResourceManager {
    model DiskOptions {
        value: int32;
    }

    model Wrapper {
        options: DiskOptions;
    }

    @route("/number")
    @get
    op GetNumber(@body foo: DiskOptions): Wrapper;
}