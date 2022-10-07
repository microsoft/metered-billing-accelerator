param name string
param properties object

resource name_resource 'Microsoft.Solutions/applications/providers/roleAssignments@2021-04-01-preview' = {
  name: name
  properties: properties
}
