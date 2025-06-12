import "@typespec/http";

using TypeSpec.Http;

model BadResponse {
    value: int32;
}

model Wrapper {
    bad: BadResponse;
}

@service(#{title: "Sample API"})
namespace SampleApi {
    @route("/number")
    @get
    op GetNumber(): Wrapper;
}