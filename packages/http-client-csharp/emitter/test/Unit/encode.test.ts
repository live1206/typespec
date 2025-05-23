vi.resetModules();

import { TestHost } from "@typespec/compiler/testing";
import { ok, strictEqual } from "assert";
import { beforeEach, describe, it, vi } from "vitest";
import { createModel } from "../../src/lib/client-model-builder.js";
import {
  createCSharpSdkContext,
  createEmitterContext,
  createEmitterTestHost,
  typeSpecCompile,
} from "./utils/test-util.js";

describe("Test encode duration", () => {
  let runner: TestHost;

  beforeEach(async () => {
    runner = await createEmitterTestHost();
  });

  it("encode iso8601 for duration query parameter ", async () => {
    const program = await typeSpecCompile(
      `
            op test(
                @query
                @encode(DurationKnownEncoding.ISO8601)
                input: duration
              ): NoContentResponse;
      `,
      runner,
    );
    // validate method parameter
    const context = createEmitterContext(program);
    const sdkContext = await createCSharpSdkContext(context);
    const root = createModel(sdkContext);
    const methodParamArray = root.clients[0].methods[0].parameters;
    strictEqual(1, methodParamArray.length);
    let type = methodParamArray[0].type;
    strictEqual(type.kind, "duration");
    strictEqual(type.name, "duration");
    strictEqual(type.crossLanguageDefinitionId, "TypeSpec.duration");
    strictEqual(type.encode, "ISO8601");
    strictEqual(type.wireType.kind, "string");
    strictEqual(type.wireType.name, "string");
    strictEqual(type.wireType.crossLanguageDefinitionId, "TypeSpec.string");
    strictEqual(type.baseType, undefined);

    // validate operation parameter
    const inputOperationParamArray = root.clients[0].methods[0].operation.parameters.filter(
      (p) => p.name === "input",
    );
    strictEqual(1, inputOperationParamArray.length);
    type = inputOperationParamArray[0].type;
    strictEqual(type.kind, "duration");
    strictEqual(type.name, "duration");
    strictEqual(type.crossLanguageDefinitionId, "TypeSpec.duration");
    strictEqual(type.encode, "ISO8601");
    strictEqual(type.wireType.kind, "string");
    strictEqual(type.wireType.name, "string");
    strictEqual(type.wireType.crossLanguageDefinitionId, "TypeSpec.string");
    strictEqual(type.baseType, undefined);
  });

  it("encode seconds-int32 for duration query parameter ", async () => {
    const program = await typeSpecCompile(
      `
            op test(
                @query
                @encode(DurationKnownEncoding.seconds, int32)
                input: duration
              ): NoContentResponse;
      `,
      runner,
    );
    const context = createEmitterContext(program);
    const sdkContext = await createCSharpSdkContext(context);
    const root = createModel(sdkContext);
    // validate method parameter
    const methodParamArray = root.clients[0].methods[0].parameters;
    strictEqual(1, methodParamArray.length);
    let type = methodParamArray[0].type;
    strictEqual(type.kind, "duration");
    strictEqual(type.name, "duration");
    strictEqual(type.crossLanguageDefinitionId, "TypeSpec.duration");
    strictEqual(type.encode, "seconds");
    strictEqual(type.wireType.kind, "int32");
    strictEqual(type.wireType.name, "int32");
    strictEqual(type.wireType.crossLanguageDefinitionId, "TypeSpec.int32");
    strictEqual(type.baseType, undefined);

    // validate operation parameter
    const inputOperationParamArray = root.clients[0].methods[0].operation.parameters.filter(
      (p) => p.name === "input",
    );
    strictEqual(1, inputOperationParamArray.length);
    type = inputOperationParamArray[0].type;
    strictEqual(type.kind, "duration");
    strictEqual(type.name, "duration");
    strictEqual(type.crossLanguageDefinitionId, "TypeSpec.duration");
    strictEqual(type.encode, "seconds");
    strictEqual(type.wireType.kind, "int32");
    strictEqual(type.wireType.name, "int32");
    strictEqual(type.wireType.crossLanguageDefinitionId, "TypeSpec.int32");
    strictEqual(type.baseType, undefined);
  });

  it("encode seconds-float for duration query parameter ", async () => {
    const program = await typeSpecCompile(
      `
            op test(
                @query
                @encode(DurationKnownEncoding.seconds, float32)
                input: duration
              ): NoContentResponse;
      `,
      runner,
    );
    const context = createEmitterContext(program);
    const sdkContext = await createCSharpSdkContext(context);
    const root = createModel(sdkContext);
    // validate method parameter
    const methodParamArray = root.clients[0].methods[0].parameters;
    strictEqual(1, methodParamArray.length);
    let type = methodParamArray[0].type;
    strictEqual(type.kind, "duration");
    strictEqual(type.name, "duration");
    strictEqual(type.crossLanguageDefinitionId, "TypeSpec.duration");
    strictEqual(type.encode, "seconds");
    strictEqual(type.wireType.kind, "float32");
    strictEqual(type.wireType.name, "float32");
    strictEqual(type.wireType.crossLanguageDefinitionId, "TypeSpec.float32");
    strictEqual(type.baseType, undefined);
    // validate operation parameter
    const inputOperationParamArray = root.clients[0].methods[0].operation.parameters.filter(
      (p) => p.name === "input",
    );
    strictEqual(1, inputOperationParamArray.length);
    type = inputOperationParamArray[0].type;
    strictEqual(type.kind, "duration");
    strictEqual(type.name, "duration");
    strictEqual(type.crossLanguageDefinitionId, "TypeSpec.duration");
    strictEqual(type.encode, "seconds");
    strictEqual(type.wireType.kind, "float32");
    strictEqual(type.wireType.name, "float32");
    strictEqual(type.wireType.crossLanguageDefinitionId, "TypeSpec.float32");
    strictEqual(type.baseType, undefined);
  });

  it("encode iso8601 on duration model property", async () => {
    const program = await typeSpecCompile(
      `
            @doc("This is a model.")
            model ISO8601DurationProperty {
                @encode(DurationKnownEncoding.ISO8601)
                value: duration;
            }

            op test(): ISO8601DurationProperty;
      `,
      runner,
    );
    const context = createEmitterContext(program);
    const sdkContext = await createCSharpSdkContext(context);
    const codeModel = createModel(sdkContext);
    const models = codeModel.models;
    const durationModel = models.find((m) => m.name === "ISO8601DurationProperty");
    ok(durationModel);
    const type = durationModel.properties[0].type;
    strictEqual(type.kind, "duration");
    strictEqual(type.name, "duration");
    strictEqual(type.crossLanguageDefinitionId, "TypeSpec.duration");
    strictEqual(type.encode, "ISO8601");
    strictEqual(type.wireType.kind, "string");
    strictEqual(type.wireType.crossLanguageDefinitionId, "TypeSpec.string");
    strictEqual(type.baseType, undefined);
  });

  it("encode seconds int32 on duration model property", async () => {
    const program = await typeSpecCompile(
      `
            @doc("This is a model.")
            model Int32SecondsDurationProperty {
                @encode(DurationKnownEncoding.seconds, int32)
                value: duration;
            }

            op test(): Int32SecondsDurationProperty;
      `,
      runner,
    );
    const context = createEmitterContext(program);
    const sdkContext = await createCSharpSdkContext(context);
    const codeModel = createModel(sdkContext);
    const models = codeModel.models;
    const durationModel = models.find((m) => m.name === "Int32SecondsDurationProperty");
    ok(durationModel);
    const type = durationModel.properties[0].type;
    strictEqual(type.kind, "duration");
    strictEqual(type.name, "duration");
    strictEqual(type.crossLanguageDefinitionId, "TypeSpec.duration");
    strictEqual(type.encode, "seconds");
    strictEqual(type.wireType.kind, "int32");
    strictEqual(type.wireType.crossLanguageDefinitionId, "TypeSpec.int32");
    strictEqual(type.baseType, undefined);
  });

  it("encode seconds float32 on duration model property", async () => {
    const program = await typeSpecCompile(
      `
            @doc("This is a model.")
            model FloatSecondsDurationProperty {
                @encode(DurationKnownEncoding.seconds, float32)
                value: duration;
            }

            op test(): FloatSecondsDurationProperty;
      `,
      runner,
    );
    const context = createEmitterContext(program);
    const sdkContext = await createCSharpSdkContext(context);
    const codeModel = createModel(sdkContext);
    const models = codeModel.models;
    const durationModel = models.find((m) => m.name === "FloatSecondsDurationProperty");
    ok(durationModel);
    const type = durationModel.properties[0].type;
    strictEqual(type.kind, "duration");
    strictEqual(type.name, "duration");
    strictEqual(type.crossLanguageDefinitionId, "TypeSpec.duration");
    strictEqual(type.encode, "seconds");
    strictEqual(type.wireType.kind, "float32");
    strictEqual(type.wireType.crossLanguageDefinitionId, "TypeSpec.float32");
    strictEqual(type.baseType, undefined);
  });
});
