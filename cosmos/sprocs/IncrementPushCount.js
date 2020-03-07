// IncrementPushCount.js
function incrementPushCount(datetimestamp) {
    var context = getContext();
    var container = context.getCollection();
    var response = context.getResponse();

    var id = datetimestamp;
    var pk = datetimestamp.substring(0, 10);

    // query for an existing item
    var filterQuery =
    {
        'query': 'SELECT * FROM Dashboard d where d.id = @id and d.timestamp = @pk',
        'parameters': [{ name: '@id', value: id }, { name: '@pk', value: pk }]
    };

    var accepted = container.queryDocuments(container.getSelfLink(), filterQuery, {},
        function (err, items) {
            if (err) throw new Error("Error" + err.message);

            if (items.length != 1) {
                // insert
                var item = {
                    id: id,
                    timestamp: pk,
                    pushCount: 1
                };

                var accepted = container.createDocument(container.getSelfLink(),
                    item,
                    function (err, itemCreated) {
                        if (err) throw new Error('Error' + err.message);
                        response.setBody(itemCreated);
                    });
                if (!accepted) throw "Unable to create item";

            } else {
                var item = items[0];
                item.pushCount++;

                var accepted = container.replaceDocument(item._self, item,
                    function (err, itemReplaced) {
                        if (err) throw new Error('Error' + err.message);
                        response.setBody(itemReplaced);
                    });

                if (!accepted) throw "Unable to update item";
            }
        });
    
    if (!accepted) throw "Unable to query item";
}
