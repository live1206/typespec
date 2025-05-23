// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// Code generated by Microsoft (R) TypeSpec Code Generator.

package tsptest.versioning.generated;

// The Java test files under 'generated' package are generated for your reference.
// If you wish to modify these files, please copy them out of the 'generated' package, and modify there.
// See https://aka.ms/azsdk/dpg/java/tests for guide on adding a test.

import com.azure.core.http.policy.HttpLogDetailLevel;
import com.azure.core.http.policy.HttpLogOptions;
import com.azure.core.test.TestMode;
import com.azure.core.test.TestProxyTestBase;
import com.azure.core.util.Configuration;
import tsptest.versioning.VersioningClient;
import tsptest.versioning.VersioningClientBuilder;

class VersioningClientTestBase extends TestProxyTestBase {
    protected VersioningClient versioningClient;

    @Override
    protected void beforeTest() {
        VersioningClientBuilder versioningClientbuilder
            = new VersioningClientBuilder().endpoint(Configuration.getGlobalConfiguration().get("ENDPOINT", "endpoint"))
                .httpClient(getHttpClientOrUsePlayback(getHttpClients().findFirst().orElse(null)))
                .httpLogOptions(new HttpLogOptions().setLogLevel(HttpLogDetailLevel.BASIC));
        if (getTestMode() == TestMode.RECORD) {
            versioningClientbuilder.addPolicy(interceptorManager.getRecordPolicy());
        }
        versioningClient = versioningClientbuilder.buildClient();

    }
}
